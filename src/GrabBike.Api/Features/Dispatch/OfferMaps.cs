using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Dispatch;

public static class OfferMaps
{
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
