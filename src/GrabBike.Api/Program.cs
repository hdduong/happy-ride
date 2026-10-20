using System.Text.Json;
using System.Text.Json.Serialization;
using GrabBike.Api.Features.Dev;
using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Maps;
using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.Infrastructure.Persistence;
using GrabBike.Api.Infrastructure.Persistence.Repositories;
using GrabBike.Api.SharedKernel;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Happy Ride API",
        Version = "v1",
        Description = "Seeded rider An Nguyen: 11111111-1111-1111-1111-111111111111. Driver Khoa Pham: 33333333-3333-3333-3333-333333333333. Send the id in the X-User-Id header.",
    });
    options.AddSecurityDefinition("X-User-Id", new OpenApiSecurityScheme
    {
        Name = "X-User-Id",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Persona id from GET /api/dev/users.",
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("X-User-Id", document)] = [],
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
            .AllowCredentials();
    });
});

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var connectionString = builder.Configuration.GetConnectionString("GrabBike")
    ?? throw new InvalidOperationException("ConnectionStrings:GrabBike is required.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IDriverRepository, DriverRepository>();
builder.Services.AddScoped<IPlaceRepository, PlaceRepository>();
builder.Services.AddScoped<IQuoteRepository, QuoteRepository>();
builder.Services.AddScoped<IRideRepository, RideRepository>();
builder.Services.AddScoped<IOfferRepository, OfferRepository>();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IMapProvider, FakeMapProvider>();
builder.Services.AddScoped<IMapService, MapService>();
builder.Services.AddScoped<IDispatchService, DispatchService>();
builder.Services.AddScoped<IRideService, RideService>();
builder.Services.AddScoped<IOfferService, OfferService>();

builder.Services.AddScoped<IDriverSimulationService, DriverSimulationService>();
builder.Services.AddScoped<IDispatchExpiryService, DispatchExpiryService>();
builder.Services.AddSingleton<IRideWriteLock, RideWriteLock>();
builder.Services.AddSingleton<IUserIdProvider, QueryUserIdProvider>();
builder.Services.AddSingleton<IRideEventPublisher, SignalRRideEventPublisher>();
builder.Services.AddSingleton<IRideNotifications, NotificationDelivery>();
builder.Services.AddHostedService<DriverSimulationWorker>();
builder.Services.AddHostedService<DispatchExpiryWorker>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DatabaseSeeder.SeedAsync(db);
}

if (app.Environment.IsDevelopment())
{
    // One Swashbuckle document, generated from the controllers. Swagger UI and Scalar both render it.
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapScalarApiReference(options =>
    {
        options.WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json");
    });
    app.MapGet("/scalar", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
    app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
}

app.UseCors();
app.UseMiddleware<DevIdentityMiddleware>();
app.MapHub<RideHub>("/hubs/ride");
app.MapControllers();

app.Run();

public partial class Program;
