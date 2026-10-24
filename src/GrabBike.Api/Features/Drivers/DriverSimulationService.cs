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
    IRideWriteLock writeLock,
    IPickupRoutes pickupRoutes,
    IPickupRouter pickupRouter,
    IDriverRoam roam) : IDriverSimulationService
{
    public async Task<List<OutboundEvent>> AdvanceAsync(DateTimeOffset now, double stepMeters, CancellationToken cancellationToken = default)
    {
        await EnsurePickupRoutesAsync(cancellationToken);
        await EnsureRoamRoutesAsync(cancellationToken);
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
                if (!pickupRoutes.TryAdvance(ride.Id, stepMeters, out var moved))
                {
                    var straight = Geo.MoveToward(driver.Lat, driver.Lng, ride.Pickup.Lat, ride.Pickup.Lng, stepMeters);
                    moved = new PickupStep(straight.Lat, straight.Lng, straight.Heading, straight.MovedM);
                }
                ApplyMovement(driver, moved.Lat, moved.Lng, moved.Heading, moved.MovedM);
                if (moved.MovedM >= 0.5)
                    events.Add(LocationEvent(ride, driver, now));
            }
            else
            {
                if (driver.CurrentRideId is Guid finishedRideId)
                    pickupRoutes.Forget(finishedRideId);
            }

            if (ride.Status == RideStatus.InProgress)
            {
                var moved = Geo.AdvanceRoute(ride.Route.Coordinates, ride.SegmentIndex, ride.SegmentOffsetM, stepMeters);
                ride.SegmentIndex = moved.Segment;
                ride.SegmentOffsetM = moved.OffsetM;
                ApplyMovement(driver, moved.Lat, moved.Lng, moved.Heading, moved.MovedM);
                if (moved.MovedM >= 0.5)
                    events.Add(LocationEvent(ride, driver, now));
            }
        }

        await RoamAsync(events, now, stepMeters, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return events;
    }

    private async Task EnsurePickupRoutesAsync(CancellationToken cancellationToken)
    {
        foreach (var trip in await drivers.ReadOnTripAsync(cancellationToken))
        {
            if (pickupRoutes.Geometry(trip.RideId) is not null)
                continue;
            var position = await drivers.ReadPositionAsync(trip.UserId, cancellationToken);
            var pickup = await rides.ReadPickupAsync(trip.RideId, cancellationToken);
            if (position is null || pickup is null || !await rides.IsAcceptedAsync(trip.RideId, cancellationToken))
                continue;
            pickupRoutes.Start(trip.RideId, await pickupRouter.ToPickupAsync(position.Value.Lat, position.Value.Lng, pickup, cancellationToken));
        }
    }

    private async Task EnsureRoamRoutesAsync(CancellationToken cancellationToken)
    {
        foreach (var pending in roam.PendingRoutes())
        {
            var target = new GeoPoint { Lat = pending.TargetLat, Lng = pending.TargetLng, Address = "Nearby" };
            var route = await pickupRouter.ToPickupAsync(pending.StartLat, pending.StartLng, target, cancellationToken);
            roam.SetRoute(pending.DriverId, pending.Ticket, route);
        }
    }

    private async Task RoamAsync(List<OutboundEvent> events, DateTimeOffset now, double stepMeters, CancellationToken cancellationToken)
    {
        foreach (var driverId in roam.Drivers())
        {
            var driver = await drivers.FindAsync(driverId, cancellationToken);
            if (driver is null || !roam.TryTarget(driverId, out _, out _))
            {
                roam.Clear(driverId);
                continue;
            }

            Ride? ride = null;
            if (driver.Status == DriverStatus.OnTrip && driver.CurrentRideId is Guid rideId)
            {
                ride = await rides.FindAsync(rideId, cancellationToken);
                if (ride is not null && ride.Status is RideStatus.Accepted or RideStatus.DriverArrived or RideStatus.InProgress)
                {
                    roam.Clear(driverId);
                    continue;
                }
            }

            if (!roam.TryAdvance(driverId, stepMeters, out var moved))
                continue;

            ApplyMovement(driver, moved.Lat, moved.Lng, moved.Heading, moved.MovedM);
            if (ride is not null && ride.Status == RideStatus.DroppedOff && moved.MovedM >= 0.5)
                events.Add(LocationEvent(ride, driver, now));
            if (moved.Finished)
            {
                driver.SpeedKmh = 0;
                roam.Clear(driverId);
            }
        }
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
