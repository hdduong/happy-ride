using System.Threading.RateLimiting;
using GrabBike.Api.Features.Dev;
using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Maps;
using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.Features.Telemetry;
using GrabBike.Api.Infrastructure.Auth;
using GrabBike.Api.Infrastructure.Observability;
using GrabBike.Api.Infrastructure.Persistence;
using GrabBike.Api.Infrastructure.Persistence.Repositories;
using GrabBike.Api.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GrabBike.Api.Infrastructure;

public static class HappyRideHost
{
    public static string AddHappyRideCore(this WebApplicationBuilder builder)
    {
        builder.AddApplicationObservability();
        builder.AddEntraApiTokens();

        string[] allowedOrigins = builder.Configuration["Cors:AllowedOrigins"]
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        if (allowedOrigins.Length == 0)
            allowedOrigins = ["http://localhost:3000"];
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .WithExposedHeaders(RequestTelemetry.TraceHeader)
                    .AllowCredentials();
            });
        });

        var connectionString = builder.Configuration.GetConnectionString("GrabBike")
            ?? throw new InvalidOperationException("ConnectionStrings:GrabBike is required.");
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));
        builder.Services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IDriverRepository, DriverRepository>();
        builder.Services.AddScoped<IPlaceRepository, PlaceRepository>();
        builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();
        builder.Services.AddScoped<IRideRepository, RideRepository>();
        builder.Services.AddScoped<IOfferRepository, OfferRepository>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<ActivityContextResolver>();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("client-activity", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Items["CurrentUser"] is User user ? user.Id.ToString("D") : "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));
        });
        builder.Services.AddSingleton<IDriverRoam, DriverRoam>();
        builder.Services.AddSingleton<IPickupRoutes, PickupRoutes>();
        builder.Services.AddSingleton<IRideWriteLock, RideWriteLock>();
        builder.Services.AddSingleton<IRideNotifications, NotificationDelivery>();
        return connectionString;
    }

    public static async Task PrepareDatabaseAsync(this WebApplication app, string connectionString)
    {
        await using var startup = await app.Services.GetRequiredService<IRideWriteLock>().AcquireAsync();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS postgis", connection);
            await command.ExecuteNonQueryAsync();
        }
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Rides" ADD COLUMN IF NOT EXISTS "ApproachRoute" text""");
        await DatabaseSeeder.SeedAsync(db);
    }

    public static WebApplication UseHappyRidePipeline(this WebApplication app)
    {
        app.UseApplicationObservability();
        app.UseCors();
        app.UseEntraApiTokens();
        app.UseMiddleware<DevIdentityMiddleware>();
        app.UseMiddleware<ActorTelemetryMiddleware>();
        app.UseRateLimiter();
        return app;
    }
}
