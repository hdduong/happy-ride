using GrabBike.Api.Features.Maps;
using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Drivers;

public interface IPickupRouter
{
    Task<GeoRoute> ToPickupAsync(double lat, double lng, GeoPoint pickup, CancellationToken cancellationToken = default);
}

public sealed class PickupRouter(IMapProvider maps, ILogger<PickupRouter> logger) : IPickupRouter
{
    public async Task<GeoRoute> ToPickupAsync(double lat, double lng, GeoPoint pickup, CancellationToken cancellationToken = default)
    {
        try
        {
            var route = await maps.RouteAsync(new GeoPoint { Lat = lat, Lng = lng }, pickup, cancellationToken);
            if (route.Geometry.Coordinates.Length >= 2)
                return route.Geometry;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Pickup route fell back to a straight line");
        }

        return PickupRoutes.Straight(lat, lng, pickup.Lat, pickup.Lng);
    }
}
