using System.Text.Json.Serialization;
using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Infrastructure;

namespace GrabBike.Realtime;

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
        builder.Services.AddRealtimeServices();

        var app = builder.Build();
        await app.PrepareDatabaseAsync(connectionString);
        app.UseHappyRidePipeline();
        app.MapHub<RideHub>("/hubs/ride");
        app.MapControllers();
        app.Run();
    }
}
