namespace GrabBike.Api.Features.Rides;

public static class RideServiceCollectionExtensions
{
    public static IServiceCollection AddRideServices(this IServiceCollection services)
    {
        services.AddScoped<IRideService, RideService>();
        return services;
    }
}
