using Azure.Messaging.ServiceBus;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Realtime;

namespace GrabBike.Api.Features.Dispatch;

public static class DispatchServiceCollectionExtensions
{
    public static IServiceCollection AddDispatchServices(this IServiceCollection services, IConfiguration configuration, bool startWorker)
    {
        services.AddScoped<IPickupRouter, StraightPickupRouter>();
        services.AddScoped<IDispatchService, DispatchService>();
        services.AddScoped<IOfferService, OfferService>();
        services.AddScoped<IDispatchExpiryService, DispatchExpiryService>();
        services.AddScoped<IDispatchMessageHandler, DispatchMessageHandler>();
        var serviceBusConnection = configuration["ServiceBus:ConnectionString"];
        var useServiceBus = string.Equals(configuration["Dispatch:Bus"], "ServiceBus", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(serviceBusConnection);
        if (useServiceBus)
        {
            services.AddSingleton(new ServiceBusClient(serviceBusConnection));
            services.AddSingleton<IDispatchBus, ServiceBusDispatchBus>();
            if (startWorker)
                services.AddHostedService<ServiceBusDispatchWorker>();
        }
        else
        {
            services.AddSingleton<IDispatchBus, InMemoryDispatchBus>();
            if (startWorker)
                services.AddHostedService<DispatchExpiryWorker>();
        }
        return services;
    }
}
