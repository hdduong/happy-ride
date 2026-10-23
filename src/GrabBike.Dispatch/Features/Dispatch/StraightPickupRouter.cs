using GrabBike.Api.Features.Drivers;
using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Dispatch;

/// <summary>
/// Dispatch can quote a straight approach without the fleet map providers.
/// The combined API registers the road-following router afterward.
/// </summary>
public sealed class StraightPickupRouter : IPickupRouter
{
    public Task<GeoRoute> ToPickupAsync(double lat, double lng, GeoPoint pickup, CancellationToken cancellationToken = default) =>
        Task.FromResult(PickupRoutes.Straight(lat, lng, pickup.Lat, pickup.Lng));
}
