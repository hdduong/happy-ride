using System.Net;
using System.Text.Json.Serialization;
using GrabBike.Api.Features.Dev;
using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Maps;
using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.Features.Telemetry;
using GrabBike.Api.Infrastructure;
using GrabBike.Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.AddHappyRideCore();
var ridesUrl = builder.Configuration["Services:Rides"];
var dispatchUrl = builder.Configuration["Services:Dispatch"];
var fleetUrl = builder.Configuration["Services:Fleet"];
var realtimeUrl = builder.Configuration["Services:Realtime"];
var split = !string.IsNullOrWhiteSpace(ridesUrl)
    && !string.IsNullOrWhiteSpace(dispatchUrl)
    && !string.IsNullOrWhiteSpace(fleetUrl)
    && !string.IsNullOrWhiteSpace(realtimeUrl);
var controllers = builder.Services.AddControllers()
    .AddApplicationPart(typeof(UserController).Assembly)
    .AddApplicationPart(typeof(ActivityController).Assembly);
if (!split)
{
    controllers
        .AddApplicationPart(typeof(RidesController).Assembly)
        .AddApplicationPart(typeof(OffersController).Assembly)
        .AddApplicationPart(typeof(MapsController).Assembly);
}
if (split)
{
    controllers.ConfigureApplicationPartManager(manager =>
    {
        var featureAssemblies = new[]
        {
            typeof(RidesController).Assembly,
            typeof(OffersController).Assembly,
            typeof(MapsController).Assembly,
            typeof(RideHub).Assembly,
        };
        foreach (var part in manager.ApplicationParts.OfType<AssemblyPart>().Where(part => featureAssemblies.Contains(part.Assembly)).ToList())
            manager.ApplicationParts.Remove(part);
    });
}
controllers.AddJsonOptions(options =>
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

if (split)
{
    builder.Services.AddSingleton<IRideEventPublisher, NoRideEventPublisher>();
    builder.Services.AddReverseProxy().LoadFromMemory(
        [
            ServiceRoute("rides", "rides", "/api/rides", 0),
            ServiceRoute("rides-rest", "rides", "/api/rides/{**catch-all}", 1),
            ServiceRoute("offers", "dispatch", "/api/offers", 0),
            ServiceRoute("offers-rest", "dispatch", "/api/offers/{**catch-all}", 1),
            ServiceRoute("map-config", "fleet", "/api/map-config", 0),
            ServiceRoute("places", "fleet", "/api/places/{**catch-all}", 1),
            ServiceRoute("geocode", "fleet", "/api/geocode/{**catch-all}", 1),
            ServiceRoute("quotes", "fleet", "/api/quotes", 0),
            ServiceRoute("map-provider", "fleet", "/api/dev/map-provider", 0),
            ServiceRoute("hub", "realtime", "/hubs/{**catch-all}", 1),
        ],
        [
            ServiceCluster("rides", ridesUrl!),
            ServiceCluster("dispatch", dispatchUrl!),
            ServiceCluster("fleet", fleetUrl!),
            ServiceCluster("realtime", realtimeUrl!, TimeSpan.FromMinutes(2)),
        ]);
}
else
{
    builder.Services.AddRideServices();
    builder.Services.AddDispatchServices(builder.Configuration, startWorker: true);
    builder.Services.AddFleetServices(builder.Configuration, startWorker: true);
    builder.Services.AddRealtimeServices();
}

var app = builder.Build();
await app.PrepareDatabaseAsync(connectionString);

app.UseSwagger();
app.UseSwaggerUI();
app.MapScalarApiReference(options =>
{
    options.WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json");
});
app.MapGet("/scalar", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();

app.UseHappyRidePipeline();
if (split)
    app.MapReverseProxy();
else
    app.MapHub<RideHub>("/hubs/ride");
app.MapControllers();
app.Run();

static RouteConfig ServiceRoute(string id, string cluster, string path, int order) => new()
{
    RouteId = id,
    ClusterId = cluster,
    Order = order,
    Match = new RouteMatch { Path = path },
};

static ClusterConfig ServiceCluster(string id, string address, TimeSpan? activityTimeout = null) => new()
{
    ClusterId = id,
    Destinations = new Dictionary<string, DestinationConfig>
    {
        [id] = new() { Address = address.TrimEnd('/') + "/" },
    },
    HttpRequest = new ForwarderRequestConfig
    {
        Version = HttpVersion.Version11,
        VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        ActivityTimeout = activityTimeout ?? TimeSpan.FromSeconds(100),
    },
};

public partial class Program;
