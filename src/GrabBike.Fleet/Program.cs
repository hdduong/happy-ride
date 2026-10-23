using System.Text.Json.Serialization;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Infrastructure;

namespace GrabBike.Fleet;

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
        builder.Services.AddFleetServices(builder.Configuration, startWorker: true);
        var realtimeUrl = builder.Configuration["Services:Realtime"];
        if (!string.IsNullOrWhiteSpace(realtimeUrl))
            builder.Services.AddRemoteRealtime(realtimeUrl);
        else
            builder.Services.AddSingleton<IRideEventPublisher, NoRideEventPublisher>();

        var app = builder.Build();
        await app.PrepareDatabaseAsync(connectionString);
        app.UseHappyRidePipeline();
        app.MapControllers();
        app.Run();
    }
}
