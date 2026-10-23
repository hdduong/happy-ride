using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Maps;
using GrabBike.Api.Features.Realtime;

namespace GrabBike.Api.Infrastructure;

public static class RemoteServiceCollectionExtensions
{
    public static IServiceCollection AddRemoteDispatch(this IServiceCollection services, string dispatchUrl, string? internalKey)
    {
        services.AddHttpClient<IDispatchBus, HttpDispatchBus>(client => Configure(client, dispatchUrl, internalKey));
        return services;
    }

    public static IServiceCollection AddRemoteRealtime(this IServiceCollection services, string realtimeUrl, string? internalKey)
    {
        services.AddHttpClient<IRideEventPublisher, HttpRideEventPublisher>(client => Configure(client, realtimeUrl, internalKey));
        return services;
    }

    public static IServiceCollection AddRemoteMapConfig(this IServiceCollection services, string realtimeUrl, string? internalKey)
    {
        services.AddHttpClient<IMapConfigPublisher, HttpMapConfigPublisher>(client => Configure(client, realtimeUrl, internalKey));
        return services;
    }

    private static void Configure(HttpClient client, string url, string? internalKey)
    {
        client.BaseAddress = new Uri(url);
        client.Timeout = TimeSpan.FromSeconds(3);
        if (!string.IsNullOrWhiteSpace(internalKey))
            client.DefaultRequestHeaders.TryAddWithoutValidation(InternalAccess.Header, internalKey);
    }
}
