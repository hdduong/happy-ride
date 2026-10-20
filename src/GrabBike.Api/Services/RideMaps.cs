namespace GrabBike.Api.Services;

public static class RideMaps
{
    public static RideView ToView(Ride ride, Driver? driver)
    {
        DriverSnapshot? snapshot = driver is null
            ? null
            : new DriverSnapshot(driver.UserId, driver.Name, driver.Plate, driver.Lat, driver.Lng);

        return new RideView(
            ride.Id,
            ride.Status.ToString(),
            ride.Pickup,
            ride.Dropoff,
            ride.DistanceM,
            ride.DurationS,
            ride.FareVnd,
            ride.Route,
            ride.Round,
            snapshot);
    }

    public static OfferDto ToOffer(Offer offer, Ride ride, Driver? driver)
    {
        var pickupDistance = driver is null
            ? 0
            : (int)Math.Round(
                Geo.HaversineMeters(driver.Lat, driver.Lng, ride.Pickup.Lat, ride.Pickup.Lng),
                MidpointRounding.AwayFromZero);

        return new OfferDto(
            offer.Id,
            ride.Id,
            ride.Pickup,
            ride.Dropoff,
            ride.DistanceM,
            ride.DurationS,
            offer.ExpiresAt,
            offer.Round,
            pickupDistance);
    }
}
