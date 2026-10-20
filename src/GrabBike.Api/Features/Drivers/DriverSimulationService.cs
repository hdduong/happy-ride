using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Drivers;

public interface IDriverSimulationService
{
    Task<List<OutboundEvent>> AdvanceAsync(DateTimeOffset now, double stepMeters, CancellationToken cancellationToken = default);
}

public sealed class DriverSimulationService(
    IDriverRepository drivers,
    IRideRepository rides,
    IUnitOfWork unitOfWork,
    IRideWriteLock writeLock) : IDriverSimulationService
{
    public async Task<List<OutboundEvent>> AdvanceAsync(DateTimeOffset now, double stepMeters, CancellationToken cancellationToken = default)
    {
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var events = new List<OutboundEvent>();
        foreach (var driver in await drivers.ListOnTripAsync(cancellationToken))
        {
            if (driver.Status != DriverStatus.OnTrip || driver.CurrentRideId is not Guid rideId)
                continue;

            var ride = await rides.FindAsync(rideId, cancellationToken);
            if (ride is null)
                continue;

            if (ride.Status == RideStatus.Accepted)
            {
                var moved = Geo.MoveToward(driver.Lat, driver.Lng, ride.Pickup.Lat, ride.Pickup.Lng, stepMeters);
                ApplyMovement(driver, moved.Lat, moved.Lng, moved.Heading, moved.MovedM);
                if (moved.MovedM >= 0.5)
                    events.Add(LocationEvent(ride, driver, now));
            }
            else if (ride.Status == RideStatus.InProgress)
            {
                var moved = Geo.AdvanceRoute(ride.Route.Coordinates, ride.SegmentIndex, ride.SegmentOffsetM, stepMeters);
                ride.SegmentIndex = moved.Segment;
                ride.SegmentOffsetM = moved.OffsetM;
                ApplyMovement(driver, moved.Lat, moved.Lng, moved.Heading, moved.MovedM);
                if (moved.MovedM >= 0.5)
                    events.Add(LocationEvent(ride, driver, now));
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return events;
    }

    private static void ApplyMovement(Driver driver, double lat, double lng, double heading, double movedM)
    {
        driver.Lat = lat;
        driver.Lng = lng;
        if (movedM >= 0.5)
        {
            driver.Heading = heading;
            driver.SpeedKmh = movedM * 3.6;
        }
        else
        {
            driver.SpeedKmh = 0;
        }
    }

    private static OutboundEvent LocationEvent(Ride ride, Driver driver, DateTimeOffset now) =>
        new OutboundEvent.Location(
            ride.RiderId,
            driver.UserId,
            new DriverLocationDto(ride.Id, driver.UserId, driver.Lat, driver.Lng, driver.Heading, driver.SpeedKmh, now));

}
