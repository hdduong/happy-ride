using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Maps;

public sealed record MapConfiguration(
    string Provider,
    string Style,
    string? StyleUrl = null,
    string? BrowserKey = null,
    string Region = "VN",
    string Language = "vi");

public static class MapStyles
{
    public const string Dark = "https://tiles.openfreemap.org/styles/dark";

    public static string VietMapDark(string tileKey) =>
        $"https://maps.vietmap.vn/maps/styles/dm/style.json?apikey={Uri.EscapeDataString(tileKey)}";
}
public sealed record MapRoute(int DistanceM, int DurationS, GeoRoute Geometry);

public interface IMapProvider
{
    MapConfiguration Configuration { get; }
    Task<IReadOnlyList<PlaceDto>> AutocompleteAsync(string? query, double? lat, double? lng, CancellationToken cancellationToken = default);
    Task<PlaceDto?> GetPlaceAsync(string id, CancellationToken cancellationToken = default);
    Task<string> ReverseAsync(double lat, double lng, CancellationToken cancellationToken = default);
    Task<MapRoute> RouteAsync(GeoPoint pickup, GeoPoint dropoff, CancellationToken cancellationToken = default);
}
