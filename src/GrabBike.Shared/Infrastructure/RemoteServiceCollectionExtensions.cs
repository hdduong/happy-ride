using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Maps;
using GrabBike.Api.Features.Realtime;

namespace GrabBike.Api.Infrastructure;

public static class RemoteServiceCollectionExtensions
{
    public static IServiceCollection AddRemoteDispatch(this IServiceCollection services, string dispatchUrl)
    {
        services.AddHttpClient<IDispatchBus, HttpDispatchBus>(client => client.BaseAddress = new Uri(dispatchUrl));
        return services;
    }

    public static IServiceCollection AddRemoteRealtime(this IServiceCollection services, string realtimeUrl)
    {
        services.AddHttpClient<IRideEventPublisher, HttpRideEventPublisher>(client => client.BaseAddress = new Uri(realtimeUrl));
        return services;
    }

    public static IServiceCollection AddRemoteMapConfig(this IServiceCollection services, string realtimeUrl)
    {
        services.AddHttpClient<IMapConfigPublisher, HttpMapConfigPublisher>(client => client.BaseAddress = new Uri(realtimeUrl));
        return services;
    }
}
