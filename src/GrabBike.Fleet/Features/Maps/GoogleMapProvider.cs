using System.Net.Http.Json;
using System.Text.Json;
using GrabBike.Api.SharedKernel;
using Microsoft.Extensions.Options;

namespace GrabBike.Api.Features.Maps;

public sealed class GoogleMapProvider(HttpClient http, IOptions<MapOptions> options) : IMapProvider
{
    public MapConfiguration Configuration => new(
        "Google",
        "light",
        StyleUrl: null,
        BrowserKey: string.IsNullOrWhiteSpace(options.Value.Google.BrowserKey) ? null : options.Value.Google.BrowserKey);

    public async Task<IReadOnlyList<PlaceDto>> AutocompleteAsync(string? query, double? lat, double? lng, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["input"] = query ?? "",
            ["includedRegionCodes"] = new[] { "vn" },
            ["languageCode"] = "vi",
        };
        if (lat is double biasLat && lng is double biasLng)
        {
            body["locationBias"] = new
            {
                circle = new
                {
                    center = new { latitude = biasLat, longitude = biasLng },
                    radius = 3000,
                },
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:autocomplete")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Goog-Api-Key", Key());
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return GoogleResponses.Autocomplete(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task<PlaceDto?> GetPlaceAsync(string id, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://places.googleapis.com/v1/places/{Uri.EscapeDataString(id)}");
        request.Headers.Add("X-Goog-Api-Key", Key());
        request.Headers.Add("X-Goog-FieldMask", "id,displayName,formattedAddress,location");
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return GoogleResponses.Place(await response.Content.ReadAsStringAsync(cancellationToken), id);
    }

    public async Task<string> ReverseAsync(double lat, double lng, CancellationToken cancellationToken = default)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var url = $"https://maps.googleapis.com/maps/api/geocode/json?latlng={lat.ToString(culture)},{lng.ToString(culture)}&key={Uri.EscapeDataString(Key())}&language=vi&region=vn";
        return GoogleResponses.Reverse(await http.GetStringAsync(url, cancellationToken));
    }

    public async Task<MapRoute> RouteAsync(GeoPoint pickup, GeoPoint dropoff, CancellationToken cancellationToken = default)
    {
        var body = RouteBody(pickup, dropoff, "TWO_WHEELER");
        var json = await PostRouteAsync(body, cancellationToken);
        var route = GoogleResponses.Route(json);
        if (route is not null)
            return route;

        json = await PostRouteAsync(RouteBody(pickup, dropoff, "DRIVE"), cancellationToken);
        return GoogleResponses.Route(json) ?? throw new InvalidOperationException("Google route response had no path.");
    }

    private async Task<string> PostRouteAsync(object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://routes.googleapis.com/directions/v2:computeRoutes")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Goog-Api-Key", Key());
        request.Headers.Add("X-Goog-FieldMask", "routes.distanceMeters,routes.duration,routes.polyline.geoJsonLinestring");
        using var response = await http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return json;
        return json;
    }

    private static object RouteBody(GeoPoint pickup, GeoPoint dropoff, string travelMode) => new
    {
        origin = new { location = new { latLng = new { latitude = pickup.Lat, longitude = pickup.Lng } } },
        destination = new { location = new { latLng = new { latitude = dropoff.Lat, longitude = dropoff.Lng } } },
        travelMode,
        regionCode = "VN",
        polylineEncoding = "GEO_JSON_LINESTRING",
    };

    private string Key()
    {
        var key = options.Value.Google.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Maps:Google:ApiKey is required.");
        return key;
    }
}

public static class GoogleResponses
{
    public static IReadOnlyList<PlaceDto> Autocomplete(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("suggestions", out var suggestions))
            return [];
        var places = new List<PlaceDto>();
        foreach (var suggestion in suggestions.EnumerateArray())
        {
            if (!suggestion.TryGetProperty("placePrediction", out var prediction))
                continue;
            var id = Text(prediction, "placeId");
            var name = prediction.TryGetProperty("structuredFormat", out var format) ? Text(format, "mainText") : null;
            name ??= Text(prediction, "text");
            if (id is null || name is null)
                continue;
            var address = format.ValueKind == JsonValueKind.Object ? Text(format, "secondaryText") ?? name : name;
            places.Add(new PlaceDto(id, name, address, 0, 0));
        }
        return places;
    }

    public static PlaceDto? Place(string json, string id)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        if (!row.TryGetProperty("location", out var location))
            return null;
        var lat = Number(location, "latitude");
        var lng = Number(location, "longitude");
        var name = row.TryGetProperty("displayName", out var display) ? Text(display, "text") : id;
        if (lat is null || lng is null || name is null)
            return null;
        return new PlaceDto(Text(row, "id") ?? id, name, Text(row, "formattedAddress") ?? name, lat.Value, lng.Value);
    }

    public static string Reverse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return "";
        return Text(results[0], "formatted_address") ?? "";
    }

    public static MapRoute? Route(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (!document.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
            return null;
        var route = routes[0];
        var distance = (int)Math.Round(Number(route, "distanceMeters") ?? 0);
        var duration = ParseDuration(Text(route, "duration"));
        if (!route.TryGetProperty("polyline", out var polyline) || !polyline.TryGetProperty("geoJsonLinestring", out var line))
            return null;
        if (!line.TryGetProperty("coordinates", out var coordinates))
            return null;
        var pairs = new List<double[]>();
        foreach (var pair in coordinates.EnumerateArray())
        {
            if (pair.GetArrayLength() < 2)
                continue;
            pairs.Add([pair[0].GetDouble(), pair[1].GetDouble()]);
        }
        return new MapRoute(distance, duration, new GeoRoute("LineString", pairs.ToArray()));
    }

    private static int ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        var digits = new string(value.TakeWhile(character => char.IsDigit(character) || character == '.').ToArray());
        return double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            ? (int)Math.Round(seconds)
            : 0;
    }

    private static string? Text(JsonElement row, string name)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("text", out var text))
            return text.GetString();
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static double? Number(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number
            : null;
}
