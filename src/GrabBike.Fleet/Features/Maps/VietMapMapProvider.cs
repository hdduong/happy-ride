using System.Text.Json;
using GrabBike.Api.SharedKernel;
using Microsoft.Extensions.Options;

namespace GrabBike.Api.Features.Maps;

public sealed class VietMapMapProvider(HttpClient http, IOptions<MapOptions> options) : IMapProvider
{
    public MapConfiguration Configuration
    {
        get
        {
            var tileKey = options.Value.VietMap.TileKey;
            return new MapConfiguration("VietMap", "light", string.IsNullOrWhiteSpace(tileKey) ? null : MapStyles.VietMap(tileKey));
        }
    }

    public async Task<IReadOnlyList<PlaceDto>> AutocompleteAsync(string? query, double? lat, double? lng, CancellationToken cancellationToken = default)
    {
        var url = $"https://maps.vietmap.vn/api/autocomplete/v3?apikey={Key()}&text={Uri.EscapeDataString(query ?? "")}";
        if (lat is double focusLat && lng is double focusLng)
            url += $"&focus={focusLat.ToString(System.Globalization.CultureInfo.InvariantCulture)},{focusLng.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var json = await http.GetStringAsync(url, cancellationToken);
        return VietMapResponses.Autocomplete(json);
    }

    public async Task<PlaceDto?> GetPlaceAsync(string id, CancellationToken cancellationToken = default)
    {
        var json = await http.GetStringAsync($"https://maps.vietmap.vn/api/place/v3?apikey={Key()}&refid={Uri.EscapeDataString(id)}", cancellationToken);
        return VietMapResponses.Place(json, id);
    }

    public async Task<string> ReverseAsync(double lat, double lng, CancellationToken cancellationToken = default)
    {
        var json = await http.GetStringAsync(
            $"https://maps.vietmap.vn/api/reverse/v3?apikey={Key()}&lat={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lng={lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            cancellationToken);
        return VietMapResponses.Reverse(json);
    }

    public async Task<MapRoute> RouteAsync(GeoPoint pickup, GeoPoint dropoff, CancellationToken cancellationToken = default)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var url = "https://maps.vietmap.vn/api/route?points_encoded=false&vehicle=motorcycle"
            + $"&apikey={Key()}"
            + $"&point={pickup.Lat.ToString(culture)},{pickup.Lng.ToString(culture)}"
            + $"&point={dropoff.Lat.ToString(culture)},{dropoff.Lng.ToString(culture)}";
        var json = await http.GetStringAsync(url, cancellationToken);
        return VietMapResponses.Route(json) ?? throw new InvalidOperationException("VietMap route response had no path.");
    }

    private string Key()
    {
        var key = options.Value.VietMap.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Maps:VietMap:ApiKey is required.");
        return Uri.EscapeDataString(key);
    }
}

public static class VietMapResponses
{
    public static IReadOnlyList<PlaceDto> Autocomplete(string json)
    {
        using var document = JsonDocument.Parse(json);
        var rows = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement
            : document.RootElement.TryGetProperty("data", out var data) ? data : default;
        if (rows.ValueKind != JsonValueKind.Array)
            return [];
        var places = new List<PlaceDto>();
        foreach (var row in rows.EnumerateArray())
        {
            var id = Text(row, "ref_id") ?? Text(row, "refid");
            var name = Text(row, "name") ?? Text(row, "display");
            if (id is null || name is null)
                continue;
            places.Add(new PlaceDto(id, name, Text(row, "address") ?? name, Number(row, "lat") ?? 0, Number(row, "lng") ?? 0));
        }
        return places;
    }

    public static PlaceDto? Place(string json, string id)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var lat = Number(row, "lat");
        var lng = Number(row, "lng");
        var name = PlaceLabel(row);
        if (lat is null || lng is null || name is null)
            return null;
        return new PlaceDto(id, name, Text(row, "address") ?? name, lat.Value, lng.Value);
    }

    public static string Reverse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().FirstOrDefault()
            : document.RootElement;
        return Text(row, "display") ?? Text(row, "address") ?? "";
    }

    public static MapRoute? Route(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("paths", out var paths) || paths.GetArrayLength() == 0)
            return null;
        var path = paths[0];
        var distance = (int)Math.Round(Number(path, "distance") ?? 0);
        var time = Number(path, "time") ?? 0;
        var duration = (int)Math.Round(time / 1000d);
        if (!path.TryGetProperty("points", out var points) || !points.TryGetProperty("coordinates", out var coordinates))
            return null;
        var line = new List<double[]>();
        foreach (var pair in coordinates.EnumerateArray())
        {
            if (pair.GetArrayLength() < 2)
                continue;
            line.Add([pair[0].GetDouble(), pair[1].GetDouble()]);
        }
        return new MapRoute(distance, duration, new GeoRoute("LineString", line.ToArray()));
    }

    private static string? PlaceLabel(JsonElement row)
    {
        var named = Text(row, "name") ?? Text(row, "display") ?? Text(row, "address");
        if (!string.IsNullOrWhiteSpace(named))
            return named;
        var parts = new[] { Text(row, "hs_num"), Text(row, "street"), Text(row, "ward"), Text(row, "district"), Text(row, "city") }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var label = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(label) ? null : label;
    }

    private static string? Text(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Number(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number
            : null;
}
