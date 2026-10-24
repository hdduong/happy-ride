using HappyRide.Api.SharedKernel;

namespace HappyRide.Api.Features.Maps;

/// <summary>Seeded places and deterministic routes for the two-browser demo.</summary>
public sealed class FakeMapProvider(IPlaceRepository places) : IMapProvider
{
    public MapConfiguration Configuration { get; } = new("Fake", "dark", MapStyles.Dark);

    public async Task<IReadOnlyList<PlaceDto>> AutocompleteAsync(
        string? query,
        double? lat,
        double? lng,
        CancellationToken cancellationToken = default)
    {
        var text = (query ?? "").Trim();
        var matches = await places.ListAsync(cancellationToken);
        if (text.Length > 0)
            matches = matches.Where(place => Matches(place, text)).ToList();

        if (lat is double biasLat && lng is double biasLng)
        {
            matches.Sort((left, right) =>
            {
                var compared = Geo.HaversineMeters(biasLat, biasLng, left.Lat, left.Lng)
                    .CompareTo(Geo.HaversineMeters(biasLat, biasLng, right.Lat, right.Lng));
                return compared != 0 ? compared : string.CompareOrdinal(left.Id, right.Id);
            });
        }

        return matches.Select(ToPlace).ToList();
    }

    public async Task<PlaceDto?> GetPlaceAsync(string id, CancellationToken cancellationToken = default)
    {
        var place = await places.FindAsync(id, cancellationToken);
        return place is null ? null : ToPlace(place);
    }

    public async Task<string> ReverseAsync(double lat, double lng, CancellationToken cancellationToken = default)
    {
        var all = await places.ListAsync(cancellationToken);
        Place? nearest = null;
        var best = double.MaxValue;
        foreach (var place in all)
        {
            var distance = Geo.HaversineMeters(lat, lng, place.Lat, place.Lng);
            if (distance < best)
            {
                best = distance;
                nearest = place;
            }
        }

        return nearest is null ? "" : $"{nearest.Name}, {nearest.Address}";
    }

    public Task<MapRoute> RouteAsync(GeoPoint pickup, GeoPoint dropoff, CancellationToken cancellationToken = default)
    {
        var straight = Geo.HaversineMeters(pickup.Lat, pickup.Lng, dropoff.Lat, dropoff.Lng);
        var distanceM = (int)Math.Round(straight * 1.3, MidpointRounding.AwayFromZero);
        var durationS = (int)Math.Round(distanceM / (20_000d / 3600d), MidpointRounding.AwayFromZero);
        return Task.FromResult(new MapRoute(distanceM, durationS, StraightRoute(pickup, dropoff)));
    }

    private static GeoRoute StraightRoute(GeoPoint from, GeoPoint to)
    {
        var coordinates = new double[6][];
        coordinates[0] = [from.Lng, from.Lat];
        for (var i = 1; i <= 4; i++)
        {
            var t = i / 5d;
            coordinates[i] =
            [
                from.Lng + (to.Lng - from.Lng) * t,
                from.Lat + (to.Lat - from.Lat) * t,
            ];
        }

        coordinates[5] = [to.Lng, to.Lat];
        return new GeoRoute("LineString", coordinates);
    }

    private static bool Matches(Place place, string query)
    {
        var label = $"{place.Name}, {place.Address}";
        return place.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || place.Address.Contains(query, StringComparison.OrdinalIgnoreCase)
            || label.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static PlaceDto ToPlace(Place place) =>
        new(place.Id, place.Name, place.Address, place.Lat, place.Lng);
}
