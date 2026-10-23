using System.Text.Json;
using System.Text.Json.Serialization;
using GrabBike.Api.Features.Maps;
using Microsoft.AspNetCore.SignalR;

namespace GrabBike.Api.Features.Realtime;

public static class RealtimeServiceCollectionExtensions
{
    public static IServiceCollection AddRealtimeServices(this IServiceCollection services)
    {
        services.AddSignalR().AddJsonProtocol(options =>
        {
            options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        services.AddSingleton<IUserIdProvider, QueryUserIdProvider>();
        services.AddSingleton<IRideEventPublisher, SignalRRideEventPublisher>();
        services.AddSingleton<IMapConfigPublisher, SignalRMapConfigPublisher>();
        return services;
    }
}
