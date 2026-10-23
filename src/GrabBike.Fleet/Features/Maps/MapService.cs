using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Maps;

public interface IMapService
{
    MapConfiguration Configuration { get; }
    Task<IReadOnlyList<PlaceDto>> AutocompleteAsync(string? query, double? lat, double? lng, CancellationToken cancellationToken = default);
    Task<PlaceDto?> GetPlaceAsync(string id, CancellationToken cancellationToken = default);
    Task<string> ReverseAsync(double lat, double lng, CancellationToken cancellationToken = default);
    Task<QuoteDto> CreateQuoteAsync(GeoPoint pickup, GeoPoint dropoff, CancellationToken cancellationToken = default);
}

public sealed class MapService(IMapProvider provider, IQuoteRepository quotes, IUnitOfWork unitOfWork) : IMapService
{
    public MapConfiguration Configuration => provider.Configuration;

    public Task<IReadOnlyList<PlaceDto>> AutocompleteAsync(string? query, double? lat, double? lng, CancellationToken cancellationToken = default) =>
        provider.AutocompleteAsync(query, lat, lng, cancellationToken);

    public Task<PlaceDto?> GetPlaceAsync(string id, CancellationToken cancellationToken = default) =>
        provider.GetPlaceAsync(id, cancellationToken);

    public Task<string> ReverseAsync(double lat, double lng, CancellationToken cancellationToken = default) =>
        provider.ReverseAsync(lat, lng, cancellationToken);

    public async Task<QuoteDto> CreateQuoteAsync(GeoPoint pickup, GeoPoint dropoff, CancellationToken cancellationToken = default)
    {
        var route = await provider.RouteAsync(pickup, dropoff, cancellationToken);
        var fareVnd = Math.Max(12_000, (int)Math.Round(route.DistanceM / 1000d * 8000d, MidpointRounding.AwayFromZero));
        var quote = new Quote
        {
            Id = Guid.NewGuid(),
            Provider = provider.Configuration.Provider,
            Pickup = pickup,
            Dropoff = dropoff,
            DistanceM = route.DistanceM,
            DurationS = route.DurationS,
            FareVnd = fareVnd,
            Route = route.Geometry,
        };
        await quotes.AddAsync(quote, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new QuoteDto(quote.Id, quote.Provider, quote.DistanceM, quote.DurationS, quote.Route, quote.FareVnd);
    }

}
