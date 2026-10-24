using HappyRide.Api.SharedKernel;
using Microsoft.Extensions.Options;

namespace HappyRide.Api.Features.Maps;

public interface IMapProviderResolver
{
    IMapProvider Current { get; }
    string? TrySelect(string provider);
}

public sealed class MapProviderResolver(
    FakeMapProvider fake,
    VietMapMapProvider vietMap,
    GoogleMapProvider google,
    IOptions<MapOptions> options,
    MapProviderOverride selection,
    ILogger<MapProviderResolver> logger) : IMapProviderResolver, IMapProvider
{
    public IMapProvider Current => Resolve(selection.Current ?? options.Value.Provider);

    public MapConfiguration Configuration => Current.Configuration;

    public string? TrySelect(string provider)
    {
        if (!Available(provider))
            return $"Map provider {provider} needs its key before it can be selected.";
        selection.Set(provider);
        logger.LogInformation("Map provider is now {Provider}", provider);
        return null;
    }

    public Task<IReadOnlyList<PlaceDto>> AutocompleteAsync(string? query, double? lat, double? lng, CancellationToken cancellationToken = default) =>
        Current.AutocompleteAsync(query, lat, lng, cancellationToken);

    public Task<PlaceDto?> GetPlaceAsync(string id, CancellationToken cancellationToken = default) =>
        Current.GetPlaceAsync(id, cancellationToken);

    public Task<string> ReverseAsync(double lat, double lng, CancellationToken cancellationToken = default) =>
        Current.ReverseAsync(lat, lng, cancellationToken);

    public Task<MapRoute> RouteAsync(GeoPoint pickup, GeoPoint dropoff, CancellationToken cancellationToken = default) =>
        Current.RouteAsync(pickup, dropoff, cancellationToken);

    private IMapProvider Resolve(string provider)
    {
        if (provider.Equals("VietMap", StringComparison.OrdinalIgnoreCase) && Available("VietMap"))
            return vietMap;
        if (provider.Equals("Google", StringComparison.OrdinalIgnoreCase) && Available("Google"))
            return google;
        if (!provider.Equals("Fake", StringComparison.OrdinalIgnoreCase))
            logger.LogWarning("Map provider {Provider} is not available. Using Fake.", provider);
        return fake;
    }

    private bool Available(string provider)
    {
        if (provider.Equals("VietMap", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(options.Value.VietMap.ApiKey) && !string.IsNullOrWhiteSpace(options.Value.VietMap.TileKey);
        if (provider.Equals("Google", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(options.Value.Google.ApiKey) && !string.IsNullOrWhiteSpace(options.Value.Google.BrowserKey);
        return provider.Equals("Fake", StringComparison.OrdinalIgnoreCase);
    }
}
