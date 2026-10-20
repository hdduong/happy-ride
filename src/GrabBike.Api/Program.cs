using System.Text.Json;
using System.Text.Json.Serialization;
using GrabBike.Api;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000")
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

builder.Services.AddSingleton<IUserIdProvider, QueryUserIdProvider>();
builder.Services.AddSingleton<RideStore>();
builder.Services.AddSingleton<RideNotifier>();
builder.Services.AddHostedService<DriverMovementService>();

var app = builder.Build();

app.UseCors();
app.UseMiddleware<DevIdentityMiddleware>();
app.MapHub<RideHub>("/hubs/ride");
app.MapGrabBikeApi();

app.Run();

public partial class Program;
