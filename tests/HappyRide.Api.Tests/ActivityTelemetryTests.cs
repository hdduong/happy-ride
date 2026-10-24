using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using HappyRide.Api.Features.Dispatch;
using HappyRide.Api.Features.Maps;
using HappyRide.Api.Features.Rides;
using HappyRide.Api.Features.Telemetry;
using HappyRide.Api.Infrastructure.Observability;
using HappyRide.Api.Infrastructure.Persistence;
using HappyRide.Api.SharedKernel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HappyRide.Api.Tests;

public sealed partial class ObservabilityTests
{
    private const string Driver = "33333333-3333-3333-3333-333333333333";
    private const string OtherRider = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task Booking_logs_new_server_GUID_and_ignores_previous_ride_header()
    {
        await using var factory = new ObservabilityFactory(postgres.Container.GetConnectionString());
        using var client = factory.CreateClient();
        var oldRideId = await SeedTelemetryRide(factory, RideStatus.Completed);
        var quoteId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Quotes.Add(new Quote
            {
                Id = quoteId, Provider = "Fake", Pickup = new GeoPoint { Lat = 10.77, Lng = 106.69 },
                Dropoff = new GeoPoint { Lat = 10.78, Lng = 106.70 }, DistanceM = 1000, DurationS = 300,
                FareVnd = 15000, Route = new GeoRoute("LineString", []),
            });
            await db.SaveChangesAsync();
        }
        var activityId = Guid.NewGuid();
        factory.Logs.Entries.Clear();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/rides") { Content = JsonContent.Create(new { quoteId }) };
        request.Headers.Add("X-User-Id", Rider);
        request.Headers.Add("X-Activity-Id", activityId.ToString());
        request.Headers.Add("X-Ride-Id", oldRideId.ToString());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedRideDto>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.RideId);
        Assert.NotEqual(oldRideId, created.RideId);
        var booked = Assert.Single(factory.Logs.Entries, entry => entry.Event.Name == "RideBooked");
        Assert.Equal(created.RideId, booked.Fields["RideId"]);
        Assert.Equal("HCMR01 Rider01", booked.Scopes["Username"]);
        Assert.Equal(activityId, booked.Scopes["ActivityId"]);
        Assert.False(booked.Scopes.ContainsKey("RideId"));
        var completion = Assert.Single(factory.Logs.Entries, entry => entry.Event.Id == 1000);
        Assert.Equal(created.RideId, completion.Scopes["RideId"]);
    }

    [Fact]
    public async Task Browser_action_attempt_and_outcome_keep_shared_GUIDs_and_server_resolved_identity()
    {
        await using var factory = new ObservabilityFactory(postgres.Container.GetConnectionString());
        using var client = factory.CreateClient();
        var rideId = await SeedTelemetryRide(factory, RideStatus.Searching);
        var sessionId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        factory.Logs.Entries.Clear();
        foreach (var eventName in new[] { "ActionRequested", "ActionSucceeded" })
        {
            using var request = ActivityRequest(Rider, new ClientActivity(activityId, sessionId, eventName, "Cancel", rideId));
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        var entries = factory.Logs.Entries.Where(entry => entry.Event.Id == 2300).ToArray();
        Assert.Equal(2, entries.Length); // A shared operation ID must not suppress the outcome.
        Assert.All(entries, entry =>
        {
            Assert.Equal(Guid.Parse(Rider), entry.Scopes["ActorId"]);
            Assert.Equal("HCMR01 Rider01", entry.Scopes["Username"]);
            Assert.Equal("rider", entry.Scopes["ActorRole"]);
            Assert.Equal("Browser", entry.Fields["Source"]);
            Assert.Equal(sessionId, entry.Scopes["AppSessionId"]);
            Assert.Equal(activityId, entry.Scopes["ActivityId"]);
            Assert.Equal(rideId, entry.Scopes["RideId"]);
        });
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(RideStatus.Searching, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Rides.FindAsync(rideId))!.Status);
    }

    [Fact]
    public async Task Telemetry_rejects_unknown_fields_invalid_GUIDs_role_actions_and_foreign_ride_or_offer()
    {
        await using var factory = new ObservabilityFactory(postgres.Container.GetConnectionString());
        using var client = factory.CreateClient();
        var rideId = await SeedTelemetryRide(factory, RideStatus.Searching);
        var offerId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Offers.Add(new Offer { Id = offerId, RideId = rideId, DriverId = Guid.Parse(Driver), Round = 1, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) });
            await db.SaveChangesAsync();
        }
        var valid = new ClientActivity(Guid.NewGuid(), Guid.NewGuid(), "AppOpened");
        factory.Logs.Entries.Clear();
        foreach (var invalid in new[]
        {
            valid with { ActivityId = Guid.Empty }, valid with { AppSessionId = Guid.Empty },
            valid with { EventName = "ArbitraryMessage" }, valid with { RideId = Guid.Empty },
            valid with { ConnectionId = new string('x', 129) },
            valid with { EventName = "RideContextStarted" },
            valid with { EventName = "ActionRequested", Action = "Start", RideId = rideId },
            valid with { EventName = "AppOpened", Action = "Book" },
        })
        {
            using var request = ActivityRequest(Rider, invalid);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var request = ActivityRequest(Rider, new { valid.ActivityId, valid.AppSessionId, valid.EventName, username = "spoofed" }))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var request = ActivityRequest(OtherRider, valid with { EventName = "RideContextStarted", RideId = rideId }))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var request = ActivityRequest(Driver, valid with { EventName = "OfferDisplayed", RideId = Guid.NewGuid(), OfferId = offerId }))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Event.Id == 2300);

        using var goodRequest = ActivityRequest(Driver, valid with
        {
            EventName = "OfferDisplayed", RideId = rideId, OfferId = offerId, ConnectionId = "signalr-connection",
        });
        using var goodResponse = await client.SendAsync(goodRequest);
        Assert.Equal(HttpStatusCode.NoContent, goodResponse.StatusCode);
        var logged = Assert.Single(factory.Logs.Entries, entry => entry.Event.Id == 2300);
        Assert.Equal("HCMD01 Driver01", logged.Scopes["Username"]);
        Assert.Equal("signalr-connection", logged.Scopes["SignalRConnectionId"]);
        Assert.Equal(offerId, logged.Scopes["OfferId"]);
        Assert.Equal(rideId, logged.Scopes["RideId"]);
    }

    [Fact]
    public async Task Request_exception_and_completion_retain_identity_after_inner_scopes_unwind()
    {
        await using var factory = new ObservabilityFactory(postgres.Container.GetConnectionString());
        using var client = factory.CreateClient();
        var rideId = await SeedTelemetryRide(factory, RideStatus.Searching);
        var sessionId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        factory.Logs.Entries.Clear();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/activity-test/failure");
        request.Headers.Add("X-User-Id", Rider);
        request.Headers.Add("X-App-Session-Id", sessionId.ToString());
        request.Headers.Add("X-Activity-Id", activityId.ToString());
        request.Headers.Add("X-Ride-Id", rideId.ToString());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        foreach (var eventId in new[] { 1000, 1001 })
        {
            var entry = Assert.Single(factory.Logs.Entries, entry => entry.Event.Id == eventId);
            Assert.Equal("HCMR01 Rider01", entry.Scopes["Username"]);
            Assert.Equal(Guid.Parse(Rider), entry.Scopes["ActorId"]);
            Assert.Equal(sessionId, entry.Scopes["AppSessionId"]);
            Assert.Equal(activityId, entry.Scopes["ActivityId"]);
            Assert.Equal(rideId, entry.Scopes["RideId"]);
        }
    }

    [Fact]
    public async Task Unrelated_or_invalid_correlation_headers_do_not_misattribute_or_break_commands()
    {
        await using var factory = new ObservabilityFactory(postgres.Container.GetConnectionString());
        using var client = factory.CreateClient();
        var rideId = await SeedTelemetryRide(factory, RideStatus.Searching);
        factory.Logs.Entries.Clear();
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/rides/current"))
        {
            request.Headers.Add("X-User-Id", OtherRider);
            request.Headers.Add("X-App-Session-Id", "bad-session");
            request.Headers.Add("X-Activity-Id", Guid.Empty.ToString());
            request.Headers.Add("X-Ride-Id", rideId.ToString());
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        var completion = Assert.Single(factory.Logs.Entries, entry => entry.Event.Id == 1000);
        Assert.Equal("HCMR02 Rider02", completion.Scopes["Username"]);
        Assert.False(completion.Scopes.ContainsKey("RideId"));
        Assert.False(completion.Scopes.ContainsKey("AppSessionId"));
        Assert.False(completion.Scopes.ContainsKey("ActivityId"));

        factory.Logs.Entries.Clear();
        using var cancel = new HttpRequestMessage(HttpMethod.Post, $"/api/rides/{rideId}/cancel");
        cancel.Headers.Add("X-User-Id", Rider);
        cancel.Headers.Add("X-Ride-Id", Guid.NewGuid().ToString()); // Resolved route wins.
        using var cancelled = await client.SendAsync(cancel);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var business = Assert.Single(factory.Logs.Entries, entry => entry.Event.Name == "RideStatusChanged");
        Assert.Equal("HCMR01 Rider01", business.Scopes["Username"]);
        Assert.Equal(rideId, business.Scopes["RideId"]);
    }

    [Fact]
    public async Task Telemetry_limits_body_size_and_rate_without_limiting_business_requests()
    {
        await using var factory = new ObservabilityFactory(postgres.Container.GetConnectionString());
        using var client = factory.CreateClient();
        using (var large = ActivityRequest(Rider, new { activityId = Guid.NewGuid(), appSessionId = Guid.NewGuid(), eventName = "AppOpened" }))
        {
            large.Content = new StringContent("{" + new string(' ', 5000)
                + $"\"activityId\":\"{Guid.NewGuid()}\",\"appSessionId\":\"{Guid.NewGuid()}\",\"eventName\":\"AppOpened\"}}", Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(large);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }
        // A different persona gets its own bounded budget.
        for (var i = 0; i < 120; i++)
        {
            using var request = ActivityRequest(Driver, new ClientActivity(Guid.NewGuid(), Guid.NewGuid(), "AppOpened"));
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        using (var request = ActivityRequest(Driver, new ClientActivity(Guid.NewGuid(), Guid.NewGuid(), "AppOpened")))
        using (var response = await client.SendAsync(request))
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        using var current = new HttpRequestMessage(HttpMethod.Get, "/api/rides/current");
        current.Headers.Add("X-User-Id", Driver);
        using var currentResponse = await client.SendAsync(current);
        Assert.Equal(HttpStatusCode.NoContent, currentResponse.StatusCode);
    }

    [Theory]
    [InlineData(Rider, "HCMR01 Rider01", RideStatus.InProgress, true)]
    [InlineData(Driver, "HCMD01 Driver01", RideStatus.InProgress, true)]
    [InlineData(Driver, "HCMD01 Driver01", RideStatus.DroppedOff, false)]
    [InlineData(Rider, "HCMR01 Rider01", RideStatus.DroppedOff, true)]
    public async Task Real_SignalR_disconnect_resolves_a_ride_started_after_connect_without_ending_it(
        string userId, string username, RideStatus status, bool hasRideContext)
    {
        await using var factory = new ObservabilityFactory(postgres.Container.GetConnectionString());
        using var client = factory.CreateClient();
        var sessionId = Guid.NewGuid();
        factory.Logs.Entries.Clear();
        var socketClient = factory.Server.CreateWebSocketClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await socketClient.ConnectAsync(new Uri($"ws://localhost/hubs/ride?userId={userId}&appSessionId={sessionId}"), timeout.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, timeout.Token);
        await socket.ReceiveAsync(new ArraySegment<byte>(new byte[256]), timeout.Token);
        await WaitForConnectionLog(factory, "ConnectionConnected", timeout.Token);
        var connected = Assert.Single(factory.Logs.Entries, entry => entry.Event.Id == 2301 && Equals(entry.Fields["EventName"], "ConnectionConnected"));
        Assert.Null(connected.Scopes["RideId"]);
        var rideId = await SeedTelemetryRide(factory, status);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token);
        await WaitForConnectionLog(factory, "ConnectionDisconnected", timeout.Token);
        var disconnected = Assert.Single(factory.Logs.Entries, entry => entry.Event.Id == 2301 && Equals(entry.Fields["EventName"], "ConnectionDisconnected"));
        Assert.Equal(username, disconnected.Scopes["Username"]);
        Assert.Equal(sessionId, disconnected.Scopes["AppSessionId"]);
        Assert.Equal(hasRideContext ? rideId : null, disconnected.Scopes["RideId"]);
        Assert.Equal(connected.Scopes["SignalRConnectionId"], disconnected.Scopes["SignalRConnectionId"]);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(status, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Rides.FindAsync(rideId))!.Status);
    }

    private static async Task WaitForConnectionLog(ObservabilityFactory factory, string eventName, CancellationToken token)
    {
        while (!factory.Logs.Entries.Any(entry => entry.Event.Id == 2301 && Equals(entry.Fields["EventName"], eventName)))
            await Task.Delay(20, token);
    }

    private static HttpRequestMessage ActivityRequest(string userId, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/telemetry/activity") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-User-Id", userId);
        return request;
    }

    private static async Task<Guid> SeedTelemetryRide(ObservabilityFactory factory, RideStatus status)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rideId = Guid.NewGuid();
        db.Rides.Add(new Ride
        {
            Id = rideId, RiderId = Guid.Parse(Rider), DriverId = status == RideStatus.Searching ? null : Guid.Parse(Driver),
            QuoteId = Guid.NewGuid(), Status = status,
            Pickup = new GeoPoint { Lat = 10.77, Lng = 106.69 }, Dropoff = new GeoPoint { Lat = 10.78, Lng = 106.70 },
            DistanceM = 1000, DurationS = 300, FareVnd = 15000,
            Route = new GeoRoute("LineString", []), CreatedAt = DateTimeOffset.UtcNow.UtcTicks,
        });
        if (status != RideStatus.Searching)
        {
            var driver = await db.Drivers.FindAsync(Guid.Parse(Driver));
            driver!.CurrentRideId = rideId;
        }
        await db.SaveChangesAsync();
        return rideId;
    }
}

[ApiController]
[Route("api/activity-test")]
public sealed class ActivityTelemetryTestController : ControllerBase
{
    [HttpPost("failure")]
    public IActionResult Failure() => throw new InvalidOperationException("test exception after actor scope");
}
