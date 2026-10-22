using GrabBike.Api.Features.Drivers;
using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Rides;

public static class RideMaps
{
    public static RideView ToView(Ride ride, Driver? driver, GeoRoute? approachRoute = null)
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
            snapshot,
            approachRoute);
    }

}
