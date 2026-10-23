using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.Features.Realtime;
using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Dispatch;

public interface IDispatchService
{
    Task AdvanceAsync(Ride ride, DateTimeOffset now, List<OutboundEvent> events, CancellationToken cancellationToken = default);
}

public sealed class DispatchService(IOfferRepository offers, IDriverRepository drivers) : IDispatchService
{
    private const double DispatchRadiusM = 3000;
    private const int OfferTtlSeconds = 30;

    public async Task AdvanceAsync(
        Ride ride,
        DateTimeOffset now,
        List<OutboundEvent> events,
        CancellationToken cancellationToken = default)
    {
        if (ride.Status != RideStatus.Searching)
            return;

        var rideOffers = await offers.ForRideAsync(ride.Id, cancellationToken);
        if (rideOffers.Any(offer => offer.Status == OfferStatus.Pending && offer.ExpiresAt > now))
            return;

        var offered = rideOffers.Select(offer => offer.DriverId).ToHashSet();
        var fleet = await drivers.ListNearbyAsync(ride.Pickup.Lat, ride.Pickup.Lng, DispatchRadiusM, cancellationToken);
        var ranked = Rank(fleet, ride.Pickup.Lat, ride.Pickup.Lng, offered);
        if (ranked.Count == 0)
        {
            MarkNoDriver(ride, events);
            return;
        }

        if (rideOffers.Count > 0)
            ride.Round++;

        var next = ranked[0].Driver;
        var offer = new Offer
        {
            Id = Guid.NewGuid(),
            RideId = ride.Id,
            DriverId = next.UserId,
            Round = ride.Round,
            Status = OfferStatus.Pending,
            ExpiresAt = now.AddSeconds(OfferTtlSeconds),
        };
        await offers.AddAsync(offer, cancellationToken);
        events.Add(new OutboundEvent.OfferReceived(offer.DriverId, OfferMaps.ToOffer(offer, ride, next)));

        events.Add(new OutboundEvent.StatusChanged(RideMaps.ToView(ride, null), ride.RiderId, null));
    }

    private static void MarkNoDriver(Ride ride, List<OutboundEvent> events)
    {
        ride.Status = RideStatus.NoDriver;
        events.Add(new OutboundEvent.StatusChanged(RideMaps.ToView(ride, null), ride.RiderId, null));
    }

    private static List<(Driver Driver, double Distance)> Rank(
        List<Driver> fleet,
        double lat,
        double lng,
        HashSet<Guid> exclude)
    {
        var ranked = new List<(Driver Driver, double Distance)>();
        foreach (var driver in fleet)
        {
            if (exclude.Contains(driver.UserId))
                continue;
            if (driver.Status != DriverStatus.Online || driver.CurrentRideId is not null)
                continue;

            var distance = Geo.HaversineMeters(lat, lng, driver.Lat, driver.Lng);
            if (distance <= DispatchRadiusM)
                ranked.Add((driver, distance));
        }

        ranked.Sort((left, right) =>
        {
            var compared = left.Distance.CompareTo(right.Distance);
            return compared != 0 ? compared : left.Driver.UserId.CompareTo(right.Driver.UserId);
        });
        return ranked;
    }
}
