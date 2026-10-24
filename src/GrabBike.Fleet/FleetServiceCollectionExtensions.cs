using GrabBike.Api.Features.Maps;

namespace GrabBike.Api.Features.Drivers;

public static class FleetServiceCollectionExtensions
{
    public static IServiceCollection AddFleetServices(this IServiceCollection services, IConfiguration configuration, bool startWorker)
    {
        services.Configure<MapOptions>(configuration.GetSection("Maps"));
        services.AddSingleton<MapProviderOverride>();
        services.AddHttpClient<VietMapMapProvider>(client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddHttpClient<GoogleMapProvider>(client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddScoped<FakeMapProvider>();
        services.AddScoped<MapProviderResolver>();
        services.AddScoped<IMapProvider>(provider => provider.GetRequiredService<MapProviderResolver>());
        services.AddScoped<IMapProviderResolver>(provider => provider.GetRequiredService<MapProviderResolver>());
        services.AddScoped<IMapService, MapService>();
        services.AddScoped<IPickupRouter, PickupRouter>();
        services.AddScoped<IDriverSimulationService, DriverSimulationService>();
        if (startWorker)
            services.AddHostedService<DriverSimulationWorker>();
        return services;
    }
}
