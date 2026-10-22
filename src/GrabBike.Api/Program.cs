using System.Text.Json;
using System.Text.Json.Serialization;
using GrabBike.Api.Features.Dev;
using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Maps;
using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.Infrastructure.Auth;
using GrabBike.Api.Infrastructure.Observability;
using GrabBike.Api.Infrastructure.Persistence;
using GrabBike.Api.Infrastructure.Persistence.Repositories;
using GrabBike.Api.SharedKernel;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Azure.Messaging.ServiceBus;
using GrabBike.Api.Features.Telemetry;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.AddApplicationObservability();

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
var entraConfigured = EntraApiAuthentication.IsConfigured(builder.Configuration);
builder.Services.AddSwaggerGen(options =>
{
    var description = "Seeded rider HCMR01 Rider01: 11111111-1111-1111-1111-111111111111. Driver HCMD01 Driver01: 33333333-3333-3333-3333-333333333333. Send the id in the X-User-Id header.";
    if (entraConfigured)
        description += " Also send Authorization: Bearer with the Entra application token.";
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Happy Ride API",
        Version = "v1",
        Description = description,
    });
    options.AddSecurityDefinition("X-User-Id", new OpenApiSecurityScheme
    {
        Name = "X-User-Id",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Persona id from GET /api/dev/users.",
    });
    if (entraConfigured)
    {
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Entra application access token. Required for /api and /hubs when AzureAd is configured.",
        });
    }
    options.AddSecurityRequirement(document =>
    {
        var requirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("X-User-Id", document)] = [],
        };
        if (entraConfigured)
            requirement[new OpenApiSecuritySchemeReference("Bearer", document)] = [];
        return requirement;
    });
});

string[] allowedOrigins = builder.Configuration["Cors:AllowedOrigins"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? [];
if (allowedOrigins.Length == 0)
{
    allowedOrigins = ["http://localhost:3000"];
}

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

builder.AddEntraApiTokens();

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var connectionString = builder.Configuration.GetConnectionString("GrabBike")
    ?? throw new InvalidOperationException("ConnectionStrings:GrabBike is required.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));
builder.Services.Configure<MapOptions>(builder.Configuration.GetSection("Maps"));
builder.Services.AddSingleton<MapProviderOverride>();
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
builder.Services.AddHttpClient<VietMapMapProvider>(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddHttpClient<GoogleMapProvider>(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddScoped<FakeMapProvider>();
builder.Services.AddScoped<MapProviderResolver>();
builder.Services.AddScoped<IMapProvider>(provider => provider.GetRequiredService<MapProviderResolver>());
builder.Services.AddScoped<IMapProviderResolver>(provider => provider.GetRequiredService<MapProviderResolver>());
builder.Services.AddScoped<IMapService, MapService>();
builder.Services.AddScoped<IDispatchService, DispatchService>();
builder.Services.AddScoped<IRideService, RideService>();
builder.Services.AddScoped<IOfferService, OfferService>();

builder.Services.AddScoped<IDriverSimulationService, DriverSimulationService>();
builder.Services.AddScoped<IDispatchExpiryService, DispatchExpiryService>();
builder.Services.AddScoped<IDispatchMessageHandler, DispatchMessageHandler>();
var serviceBusConnection = builder.Configuration["ServiceBus:ConnectionString"];
var useServiceBus = string.Equals(builder.Configuration["Dispatch:Bus"], "ServiceBus", StringComparison.OrdinalIgnoreCase)
    && !string.IsNullOrWhiteSpace(serviceBusConnection);
if (useServiceBus)
{
    builder.Services.AddSingleton(new ServiceBusClient(serviceBusConnection));
    builder.Services.AddSingleton<IDispatchBus, ServiceBusDispatchBus>();
    builder.Services.AddHostedService<ServiceBusDispatchWorker>();
}
else
{
    builder.Services.AddSingleton<IDispatchBus, InMemoryDispatchBus>();
    builder.Services.AddHostedService<DispatchExpiryWorker>();
}
builder.Services.AddSingleton<IPickupRoutes, PickupRoutes>();
builder.Services.AddScoped<IPickupRouter, PickupRouter>();
builder.Services.AddSingleton<IRideWriteLock, RideWriteLock>();
builder.Services.AddSingleton<IUserIdProvider, QueryUserIdProvider>();
builder.Services.AddSingleton<IRideEventPublisher, SignalRRideEventPublisher>();
builder.Services.AddSingleton<IRideNotifications, NotificationDelivery>();
builder.Services.AddSingleton<IMapConfigPublisher, SignalRMapConfigPublisher>();
builder.Services.AddHostedService<DriverSimulationWorker>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await using (var connection = new NpgsqlConnection(connectionString))
    {
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS postgis", connection);
        await command.ExecuteNonQueryAsync();
    }
    await db.Database.EnsureCreatedAsync();
    await DatabaseSeeder.SeedAsync(db);
}

app.UseApplicationObservability();

// One Swashbuckle document, generated from the controllers. Swagger UI and Scalar both render it.
// These routes stay available in Production; they are not a Development-only convenience.
app.UseSwagger();
app.UseSwaggerUI();
app.MapScalarApiReference(options =>
{
    options.WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json");
});
app.MapGet("/scalar", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

app.UseCors();
app.UseEntraApiTokens();
app.UseMiddleware<DevIdentityMiddleware>();
app.UseMiddleware<ActorTelemetryMiddleware>();
app.UseRateLimiter();
app.MapHub<RideHub>("/hubs/ride");
app.MapControllers();

app.Run();

public partial class Program;
