using System.Text.Json;
using GrabBike.Api.Features.Maps;

namespace GrabBike.Api.Tests;

public class MapProviderParsingTests
{
    [Fact]
    public void VietMap_route_reads_motorcycle_coordinates()
    {
        const string json = """
            {"paths":[{"distance":1300,"time":9000,"points":{"coordinates":[[106.7,10.77],[106.71,10.78]]}}]}
            """;
        var route = VietMapResponses.Route(json);
        Assert.NotNull(route);
        Assert.Equal(1300, route.DistanceM);
        Assert.Equal(9, route.DurationS);
        Assert.Equal(2, route.Geometry.Coordinates.Length);
    }

    [Fact]
    public void VietMap_place_without_a_name_uses_the_address_parts()
    {
        const string json = """
            {"lat":10.77,"lng":106.7,"hs_num":"2","street":"Hai Trieu","ward":"Ben Nghe","district":"1","city":"Ho Chi Minh"}
            """;
        var place = VietMapResponses.Place(json, "ref-1");
        Assert.NotNull(place);
        Assert.Equal("2, Hai Trieu, Ben Nghe, 1, Ho Chi Minh", place.Name);
        Assert.Equal(10.77, place.Lat);
    }

    [Fact]
    public void Google_route_reads_geojson_duration()
    {
        const string json = """
            {"routes":[{"distanceMeters":900,"duration":"120s","polyline":{"geoJsonLinestring":{"type":"LineString","coordinates":[[106.7,10.77],[106.8,10.78]]}}}]}
            """;
        var route = GoogleResponses.Route(json);
        Assert.NotNull(route);
        Assert.Equal(900, route.DistanceM);
        Assert.Equal(120, route.DurationS);
    }

    [Fact]
    public void Fake_map_config_exposes_a_dark_style_and_no_browser_key()
    {
        var config = new MapConfiguration("Fake", "dark", MapStyles.Dark);
        Assert.Contains("dark", config.StyleUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Null(config.BrowserKey);
        Assert.DoesNotContain("apikey", config.StyleUrl, StringComparison.OrdinalIgnoreCase);
    }
}
