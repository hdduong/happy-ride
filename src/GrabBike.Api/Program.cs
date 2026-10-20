using System.Text.Json;
using System.Text.Json.Serialization;
using GrabBike.Api;
using GrabBike.Api.Data;
using GrabBike.Api.Middleware;
using GrabBike.Api.Repositories;
using GrabBike.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
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

builder.Services.AddScoped<IDevService, DevService>();
builder.Services.AddScoped<IMapService, MapService>();
builder.Services.AddScoped<IDispatchService, DispatchService>();
builder.Services.AddScoped<IRideService, RideService>();
builder.Services.AddScoped<IOfferService, OfferService>();

builder.Services.AddSingleton<RideWriteLock>();
builder.Services.AddSingleton<IUserIdProvider, QueryUserIdProvider>();
builder.Services.AddSingleton<RideNotifier>();
builder.Services.AddHostedService<DriverMovementService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DatabaseSeeder.SeedAsync(db);
}

app.UseCors();
app.UseMiddleware<DevIdentityMiddleware>();
app.MapHub<RideHub>("/hubs/ride");
app.MapControllers();

app.Run();

public partial class Program;
