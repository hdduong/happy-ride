using GrabBike.Api.Data;
using GrabBike.Api.Repositories;

namespace GrabBike.Api.Services;

public interface IMapService
{
    Task<IReadOnlyList<PlaceDto>> AutocompleteAsync(string? query, double? lat, double? lng, CancellationToken cancellationToken = default);
    Task<PlaceDto?> GetPlaceAsync(string id, CancellationToken cancellationToken = default);
    Task<string> ReverseAsync(double lat, double lng, CancellationToken cancellationToken = default);
    Task<QuoteDto> CreateQuoteAsync(GeoPoint pickup, GeoPoint dropoff, CancellationToken cancellationToken = default);
}

public sealed class MapService(IPlaceRepository places, IQuoteRepository quotes, IUnitOfWork unitOfWork) : IMapService
{
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

    public async Task<QuoteDto> CreateQuoteAsync(GeoPoint pickup, GeoPoint dropoff, CancellationToken cancellationToken = default)
    {
        var (distanceM, durationS, fareVnd) = Geo.Estimate(pickup, dropoff);
        var quote = new Quote
        {
            Id = Guid.NewGuid(),
            Provider = "Fake",
            Pickup = pickup,
            Dropoff = dropoff,
            DistanceM = distanceM,
            DurationS = durationS,
            FareVnd = fareVnd,
            Route = Geo.StraightRoute(pickup, dropoff),
        };
        await quotes.AddAsync(quote, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new QuoteDto(quote.Id, quote.Provider, quote.DistanceM, quote.DurationS, quote.Route, quote.FareVnd);
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
