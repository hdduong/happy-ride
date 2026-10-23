using System.Text.Json.Serialization;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.Infrastructure;

namespace GrabBike.Rides;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        var connectionString = builder.AddHappyRideCore();
        var dispatchUrl = builder.Configuration["Services:Dispatch"]
            ?? throw new InvalidOperationException("Services:Dispatch is required for the rides service.");
        var realtimeUrl = builder.Configuration["Services:Realtime"]
            ?? throw new InvalidOperationException("Services:Realtime is required for the rides service.");
        builder.Services.AddRideServices();
        builder.Services.AddRemoteDispatch(dispatchUrl);
        builder.Services.AddRemoteRealtime(realtimeUrl);

        var app = builder.Build();
        await app.PrepareDatabaseAsync(connectionString);
        app.UseHappyRidePipeline();
        app.MapControllers();
        app.Run();
    }
}
