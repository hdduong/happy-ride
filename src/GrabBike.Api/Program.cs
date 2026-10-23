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
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.AddHappyRideCore();
builder.Services.AddControllers()
    .AddApplicationPart(typeof(RidesController).Assembly)
    .AddApplicationPart(typeof(OffersController).Assembly)
    .AddApplicationPart(typeof(MapsController).Assembly)
    .AddApplicationPart(typeof(UserController).Assembly)
    .AddApplicationPart(typeof(ActivityController).Assembly)
    .AddJsonOptions(options =>
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

builder.Services.AddRideServices();
builder.Services.AddDispatchServices(builder.Configuration, startWorker: true);
builder.Services.AddFleetServices(builder.Configuration, startWorker: true);
builder.Services.AddRealtimeServices();

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
app.MapHub<RideHub>("/hubs/ride");
app.MapControllers();
app.Run();

public partial class Program;
