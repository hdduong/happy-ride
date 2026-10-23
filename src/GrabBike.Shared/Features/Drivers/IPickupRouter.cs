using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Drivers;

public interface IPickupRouter
{
    Task<GeoRoute> ToPickupAsync(double lat, double lng, GeoPoint pickup, CancellationToken cancellationToken = default);
}
