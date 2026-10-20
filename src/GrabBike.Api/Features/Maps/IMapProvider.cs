using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Maps;

public sealed record MapConfiguration(string Provider, string Style);
public sealed record MapRoute(int DistanceM, int DurationS, GeoRoute Geometry);

public interface IMapProvider
{
    MapConfiguration Configuration { get; }
    Task<IReadOnlyList<PlaceDto>> AutocompleteAsync(string? query, double? lat, double? lng, CancellationToken cancellationToken = default);
    Task<PlaceDto?> GetPlaceAsync(string id, CancellationToken cancellationToken = default);
    Task<string> ReverseAsync(double lat, double lng, CancellationToken cancellationToken = default);
    Task<MapRoute> RouteAsync(GeoPoint pickup, GeoPoint dropoff, CancellationToken cancellationToken = default);
}
