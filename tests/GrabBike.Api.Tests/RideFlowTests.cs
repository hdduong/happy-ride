using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Infrastructure.Persistence;
using GrabBike.Api.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GrabBike.Api.Tests;

[Collection(PostgresCollection.Name)]
public class RideFlowTests(PostgresFixture postgres)
{
    private string Database => postgres.Container.GetConnectionString();
    private const string An = "11111111-1111-1111-1111-111111111111";
    private const string Khoa = "33333333-3333-3333-3333-333333333333";
    private const string Linh = "44444444-4444-4444-4444-444444444444";
    private const string Duc = "55555555-5555-5555-5555-555555555555";
    private const string Ha = "66666666-6666-6666-6666-666666666666";
    private const string Nam = "77777777-7777-7777-7777-777777777777";

    private static readonly Pin BenThanh = new(10.77254, 106.69800, "Chợ Bến Thành, District 1");
    private static readonly Pin Bitexco = new(10.77175, 106.70446, "Bitexco Financial Tower, 2 Hai Trieu, District 1");

    [Fact]
    public async Task OpenApi_document_feeds_swagger_and_scalar()
    {
        await using var fixture = new ApiFactory(Database, environment: Environments.Production);
        var client = fixture.CreateClient();

        var document = await client.GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("\"/api/rides\"", document);
        Assert.Contains("\"/api/dev/users\"", document);

        using var direct = fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var path in new[] { "/", "/scalar" })
        {
            var redirect = await direct.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
            Assert.Equal("/scalar/v1", redirect.Headers.Location?.OriginalString);
        }

        var scalar = await client.GetAsync("/scalar/v1");
        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
        var page = await scalar.Content.ReadAsStringAsync();
        Assert.Contains("swagger/v1/swagger.json", page);

        var swagger = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
    }

    [Fact]
    public async Task Quote_for_Ben_Thanh_to_Bitexco_returns_distance_fare_and_route()
    {
        await using var fixture = new ApiFactory(Database);
        var response = await fixture.Client(An).PostAsJsonAsync("/api/quotes", new
        {
            pickup = BenThanh,
            dropoff = Bitexco,
        });
        var body = await Read(response, HttpStatusCode.Created);

        Assert.Equal("Fake", body.GetProperty("provider").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("quoteId").GetGuid());

        var straight = HaversineMeters(BenThanh.Lat, BenThanh.Lng, Bitexco.Lat, Bitexco.Lng);
        var expectedDistance = (int)Math.Round(straight * 1.3, MidpointRounding.AwayFromZero);
        var expectedDuration = (int)Math.Round(expectedDistance / (20_000d / 3600d), MidpointRounding.AwayFromZero);
        var expectedFare = Math.Max(12_000, (int)Math.Round(expectedDistance / 1000d * 8000d, MidpointRounding.AwayFromZero));

        Assert.Equal(expectedDistance, body.GetProperty("distanceM").GetInt32());
        Assert.Equal(expectedDuration, body.GetProperty("durationS").GetInt32());
        Assert.Equal(expectedFare, body.GetProperty("fareVnd").GetInt32());
        Assert.True(body.GetProperty("distanceM").GetInt32() > straight);

        var route = body.GetProperty("route");
        Assert.Equal("LineString", route.GetProperty("type").GetString());
        var coordinates = route.GetProperty("coordinates").EnumerateArray().Select(Point).ToList();
        Assert.Equal(6, coordinates.Count);
        Assert.Equal(BenThanh.Lng, coordinates[0].Lng, 6);
        Assert.Equal(BenThanh.Lat, coordinates[0].Lat, 6);
        Assert.Equal(Bitexco.Lng, coordinates[^1].Lng, 6);
        Assert.Equal(Bitexco.Lat, coordinates[^1].Lat, 6);
        Assert.NotEqual(coordinates[0].Lng, coordinates[3].Lng);
    }

    [Fact]
    public async Task Post_ride_as_An_creates_Searching_and_offers_Khoa_and_others()
    {
        await using var fixture = new ApiFactory(Database);
        var rideId = await CreateRide(fixture.Client(An));

        var ride = await Read(await fixture.Client(An).GetAsync($"/api/rides/{rideId}"), HttpStatusCode.OK);
        Assert.Equal("Searching", ride.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, ride.GetProperty("driver").ValueKind);

        var khoaOffer = await PendingOffer(fixture.Client(Khoa), rideId);
        Assert.NotEqual(Guid.Empty, khoaOffer);
        Assert.Equal(Guid.Empty, await PendingOffer(fixture.Client(Linh), rideId));
        Assert.Equal(Guid.Empty, await PendingOffer(fixture.Client(Duc), rideId));
        Assert.Equal(Guid.Empty, await PendingOffer(fixture.Client(Ha), rideId));

        var offers = await Read(await fixture.Client(Khoa).GetAsync("/api/offers/pending"), HttpStatusCode.OK);
        var offer = offers.EnumerateArray().Single(item => item.GetProperty("rideId").GetGuid() == rideId);
        Assert.Equal(1, offer.GetProperty("round").GetInt32());
        Assert.True(offer.GetProperty("distanceM").GetInt32() > 0);
        Assert.True(offer.GetProperty("pickupDistanceM").GetInt32() > 0);
        Assert.True(offer.GetProperty("pickupDistanceM").GetInt32() < offer.GetProperty("distanceM").GetInt32());
        Assert.True(offer.GetProperty("durationS").GetInt32() > 0);
        Assert.Equal("Chợ Bến Thành, District 1", offer.GetProperty("pickup").GetProperty("address").GetString());
        var ttl = offer.GetProperty("expiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow;
        Assert.InRange(ttl.TotalSeconds, 20, 35);
    }

    [Fact]
    public async Task Two_concurrent_accepts_yield_one_success_and_one_conflict()
    {
        await using var fixture = new ApiFactory(Database);
        var rideId = await CreateRide(fixture.Client(An));
        var khoaOffer = await PendingOffer(fixture.Client(Khoa), rideId);

        var first = fixture.Client(Khoa).PostAsync($"/api/offers/{khoaOffer}/accept", content: null);
        var second = fixture.Client(Khoa).PostAsync($"/api/offers/{khoaOffer}/accept", content: null);
        var responses = await Task.WhenAll(first, second);

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var body = await Read(conflict, HttpStatusCode.Conflict);
        Assert.Equal("Taken", body.GetProperty("reason").GetString());

        var winner = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
        var view = await Read(winner, HttpStatusCode.OK);
        Assert.Equal("Accepted", view.GetProperty("status").GetString());
        var driverId = view.GetProperty("driver").GetProperty("userId").GetGuid();
        Assert.Equal(Guid.Parse(Khoa), driverId);
    }

    [Fact]
    public async Task Delayed_routing_cannot_accept_one_offer_twice()
    {
        var gate = new GatedPickupRouter(2);
        await using var fixture = new ApiFactory(Database, pickupRouter: gate);
        var rideId = await CreateRide(fixture.Client(An));
        var offerId = await PendingOffer(fixture.Client(Khoa), rideId);

        var first = fixture.Client(Khoa).PostAsync($"/api/offers/{offerId}/accept", content: null);
        var second = fixture.Client(Khoa).PostAsync($"/api/offers/{offerId}/accept", content: null);
        await gate.AllEntered.WaitAsync(TimeSpan.FromSeconds(20));
        gate.Release();
        var responses = await Task.WhenAll(first, second);

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        var ride = await Read(await fixture.Client(An).GetAsync($"/api/rides/{rideId}"), HttpStatusCode.OK);
        Assert.Equal("Accepted", ride.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Delayed_accept_does_not_revive_a_cancelled_ride()
    {
        var gate = new GatedPickupRouter(1);
        await using var fixture = new ApiFactory(Database, pickupRouter: gate);
        var rideId = await CreateRide(fixture.Client(An));
        var offerId = await PendingOffer(fixture.Client(Khoa), rideId);

        var accept = fixture.Client(Khoa).PostAsync($"/api/offers/{offerId}/accept", content: null);
        await gate.AllEntered.WaitAsync(TimeSpan.FromSeconds(20));
        var cancelled = await Read(await fixture.Client(An).PostAsync($"/api/rides/{rideId}/cancel", content: null), HttpStatusCode.OK);
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        gate.Release();

        var acceptResponse = await accept;
        Assert.NotEqual(HttpStatusCode.OK, acceptResponse.StatusCode);
        var ride = await Read(await fixture.Client(An).GetAsync($"/api/rides/{rideId}"), HttpStatusCode.OK);
        Assert.Equal("Cancelled", ride.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Happy_path_completes_and_returns_driver_online()
    {
        await using var fixture = new ApiFactory(Database);
        var an = fixture.Client(An);
        var khoa = fixture.Client(Khoa);
        var rideId = await CreateRide(an);
        var offerId = await PendingOffer(khoa, rideId);

        var accepted = await Read(await khoa.PostAsync($"/api/offers/{offerId}/accept", content: null), HttpStatusCode.OK);
        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());
        Assert.Equal(Guid.Parse(Khoa), accepted.GetProperty("driver").GetProperty("userId").GetGuid());
        Assert.Equal("59-H1 234.56", accepted.GetProperty("driver").GetProperty("plate").GetString());

        Assert.Equal("DriverArrived", await StatusAfter(khoa, $"/api/rides/{rideId}/arrived"));
        Assert.Equal("InProgress", await StatusAfter(khoa, $"/api/rides/{rideId}/start"));
        Assert.Equal("DroppedOff", await StatusAfter(khoa, $"/api/rides/{rideId}/dropoff"));

        var completed = await Read(await an.PostAsync($"/api/rides/{rideId}/confirm-arrival", content: null), HttpStatusCode.OK);
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
        var driverLat = completed.GetProperty("driver").GetProperty("lat").GetDouble();
        var driverLng = completed.GetProperty("driver").GetProperty("lng").GetDouble();

        var quote = await Read(await an.PostAsJsonAsync("/api/quotes", new
        {
            pickup = new Pin(driverLat, driverLng, "Driver"),
            dropoff = new Pin(driverLat + 0.01, driverLng + 0.01, "Nearby"),
        }), HttpStatusCode.Created);
        var nextRide = await Read(await an.PostAsJsonAsync("/api/rides", new
        {
            quoteId = quote.GetProperty("quoteId").GetGuid(),
        }), HttpStatusCode.Created);

        var nextOffer = await PendingOffer(khoa, nextRide.GetProperty("rideId").GetGuid());
        Assert.NotEqual(Guid.Empty, nextOffer);
    }

    [Fact]
    public async Task Confirm_before_dropoff_returns_invalid_transition()
    {
        await using var fixture = new ApiFactory(Database);
        var an = fixture.Client(An);
        var khoa = fixture.Client(Khoa);
        var rideId = await CreateRide(an);
        var offerId = await PendingOffer(khoa, rideId);
        await Read(await khoa.PostAsync($"/api/offers/{offerId}/accept", content: null), HttpStatusCode.OK);

        var response = await an.PostAsync($"/api/rides/{rideId}/confirm-arrival", content: null);
        var body = await Read(response, HttpStatusCode.Conflict);
        Assert.Equal("InvalidTransition", body.GetProperty("error").GetString());
        Assert.Equal("Accepted", body.GetProperty("from").GetString());
        Assert.Equal("Completed", body.GetProperty("to").GetString());

        var ride = await Read(await an.GetAsync($"/api/rides/{rideId}"), HttpStatusCode.OK);
        Assert.Equal("Accepted", ride.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Second_ride_while_one_is_active_is_rejected()
    {
        await using var fixture = new ApiFactory(Database);
        var an = fixture.Client(An);
        var rideId = await CreateRide(an);
        var quote = await Read(await an.PostAsJsonAsync("/api/quotes", new
        {
            pickup = BenThanh,
            dropoff = Bitexco,
        }), HttpStatusCode.Created);
        var again = await an.PostAsJsonAsync("/api/rides", new
        {
            quoteId = quote.GetProperty("quoteId").GetGuid(),
        });
        var body = await Read(again, HttpStatusCode.Conflict);
        Assert.Equal("ActiveRide", body.GetProperty("error").GetString());
        Assert.Equal(rideId, body.GetProperty("rideId").GetGuid());

        var current = await Read(await an.GetAsync("/api/rides/current"), HttpStatusCode.OK);
        Assert.Equal(rideId, current.GetProperty("rideId").GetGuid());
    }

    [Fact]
    public async Task Ride_outside_every_cluster_becomes_NoDriver()
    {
        await using var fixture = new ApiFactory(Database);
        var quote = await Read(await fixture.Client(An).PostAsJsonAsync("/api/quotes", new
        {
            pickup = new Pin(16.047, 108.206, "Da Nang"),
            dropoff = new Pin(16.067, 108.220, "Nearby"),
        }), HttpStatusCode.Created);
        var created = await Read(await fixture.Client(An).PostAsJsonAsync("/api/rides", new
        {
            quoteId = quote.GetProperty("quoteId").GetGuid(),
        }), HttpStatusCode.Created);

        var ride = await Read(
            await fixture.Client(An).GetAsync($"/api/rides/{created.GetProperty("rideId").GetGuid()}"),
            HttpStatusCode.OK);
        Assert.Equal("NoDriver", ride.GetProperty("status").GetString());
        Assert.Equal(Guid.Empty, await PendingOffer(fixture.Client(Khoa), created.GetProperty("rideId").GetGuid()));
        var current = await Read(await fixture.Client(An).GetAsync("/api/rides/current"), HttpStatusCode.OK);
        Assert.Equal("NoDriver", current.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Declining_the_first_round_offers_the_remaining_driver()
    {
        await using var fixture = new ApiFactory(Database);
        var rideId = await CreateRide(fixture.Client(An));
        var queue = new[] { Khoa, Linh, Duc, Ha };
        for (var index = 0; index < queue.Length - 1; index++)
        {
            var offerId = await PendingOffer(fixture.Client(queue[index]), rideId);
            Assert.NotEqual(Guid.Empty, offerId);
            Assert.Equal(Guid.Empty, await PendingOffer(fixture.Client(queue[index + 1]), rideId));
            await Read(await fixture.Client(queue[index]).PostAsync($"/api/offers/{offerId}/decline", content: null), HttpStatusCode.OK);
        }

        var haOffers = await Read(await fixture.Client(Ha).GetAsync("/api/offers/pending"), HttpStatusCode.OK);
        var haOffer = Assert.Single(haOffers.EnumerateArray(), item => item.GetProperty("rideId").GetGuid() == rideId);
        Assert.Equal(4, haOffer.GetProperty("round").GetInt32());

        var ride = await Read(await fixture.Client(An).GetAsync($"/api/rides/{rideId}"), HttpStatusCode.OK);
        Assert.Equal("Searching", ride.GetProperty("status").GetString());
        Assert.Equal(4, ride.GetProperty("round").GetInt32());
    }

    [Fact]
    public async Task Hanoi_rider_at_Lau_ech_Ha_My_offers_Doi_Can_before_Kim_Ma()
    {
        await using var fixture = new ApiFactory(Database);
        var quote = await Read(await fixture.Client(Nam).PostAsJsonAsync("/api/quotes", new
        {
            pickup = new Pin(21.03342, 105.82845, "Lẩu Ếch Hà My"),
            dropoff = new Pin(21.03523, 105.82096, "Phố Đội Cấn"),
        }), HttpStatusCode.Created);
        var created = await Read(await fixture.Client(Nam).PostAsJsonAsync("/api/rides", new
        {
            quoteId = quote.GetProperty("quoteId").GetGuid(),
        }), HttpStatusCode.Created);
        var rideId = created.GetProperty("rideId").GetGuid();

        var ride = await Read(await fixture.Client(Nam).GetAsync($"/api/rides/{rideId}"), HttpStatusCode.OK);
        Assert.Equal("Searching", ride.GetProperty("status").GetString());
        Assert.NotEqual(Guid.Empty, await PendingOffer(fixture.Client("99999999-9999-9999-9999-999999999999"), rideId));
        Assert.Equal(Guid.Empty, await PendingOffer(fixture.Client("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), rideId));
        Assert.Equal(Guid.Empty, await PendingOffer(fixture.Client(Khoa), rideId));
    }

    [Fact]
    public async Task A_driver_rolls_to_a_nearby_place_after_drop_off()
    {
        await using var fixture = new ApiFactory(Database);
        var id = Guid.Parse(Khoa);
        double startLat;
        double startLng;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var driver = await db.Drivers.FindAsync([id]);
            Assert.NotNull(driver);
            startLat = driver.Lat;
            startLng = driver.Lng;
            scope.ServiceProvider.GetRequiredService<IDriverRoam>().Begin(id, startLat, startLng);
        }

        for (var step = 0; step < 40; step++)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            var simulation = scope.ServiceProvider.GetRequiredService<IDriverSimulationService>();
            await simulation.AdvanceAsync(DateTimeOffset.UtcNow, 20, CancellationToken.None);
        }

        await using var check = fixture.Services.CreateAsyncScope();
        var parked = await check.ServiceProvider.GetRequiredService<AppDbContext>().Drivers.FindAsync([id]);
        Assert.NotNull(parked);
        Assert.InRange(HaversineMeters(startLat, startLng, parked.Lat, parked.Lng), 180, 470);
    }

    [Fact]
    public async Task Choosing_a_persona_returns_an_idle_driver_to_the_seeded_place()
    {
        await using var fixture = new ApiFactory(Database);
        var id = Guid.Parse(Khoa);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var driver = await db.Drivers.FindAsync([id]);
            Assert.NotNull(driver);
            driver.Lat = 10.8;
            driver.Lng = 106.8;
            driver.Heading = 90;
            driver.SpeedKmh = 30;
            await db.SaveChangesAsync();
        }

        await Read(await fixture.Client(Khoa).PostAsync($"/api/dev/users/{Khoa}/home", null), HttpStatusCode.NoContent);
        await Read(await fixture.Client(An).PostAsync($"/api/dev/users/{An}/home", null), HttpStatusCode.NoContent);

        var users = await Read(await fixture.Client(Khoa).GetAsync("/api/dev/users"), HttpStatusCode.OK);
        var row = users.EnumerateArray().Single(user => user.GetProperty("id").GetGuid() == id);
        Assert.Equal(10.7735, row.GetProperty("lat").GetDouble(), 5);
        Assert.Equal(106.6990, row.GetProperty("lng").GetDouble(), 5);

        await using var check = fixture.Services.CreateAsyncScope();
        var parked = await check.ServiceProvider.GetRequiredService<AppDbContext>().Drivers.FindAsync([id]);
        Assert.NotNull(parked);
        Assert.Equal(0, parked.Heading);
        Assert.Equal(0, parked.SpeedKmh);
    }

    [Fact]
    public async Task Choosing_a_driver_on_a_trip_leaves_their_place()
    {
        await using var fixture = new ApiFactory(Database);
        var id = Guid.Parse(Khoa);
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var driver = await db.Drivers.FindAsync([id]);
            Assert.NotNull(driver);
            driver.Lat = 10.8;
            driver.Lng = 106.8;
            driver.Status = DriverStatus.OnTrip;
            await db.SaveChangesAsync();
        }

        await Read(await fixture.Client(Khoa).PostAsync($"/api/dev/users/{Khoa}/home", null), HttpStatusCode.NoContent);

        await using var check = fixture.Services.CreateAsyncScope();
        var moved = await check.ServiceProvider.GetRequiredService<AppDbContext>().Drivers.FindAsync([id]);
        Assert.NotNull(moved);
        Assert.Equal(10.8, moved.Lat, 5);
        Assert.Equal(106.8, moved.Lng, 5);
    }

    [Fact]
    public async Task Startup_moves_HanoiD01_only_from_the_old_seed_and_never_during_a_trip()
    {
        await using var fixture = new ApiFactory(Database);
        var id = Guid.Parse("99999999-9999-9999-9999-999999999999");
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var driver = await db.Drivers.FindAsync([id]);
        Assert.NotNull(driver);
        Assert.Equal(21.0352316, driver.Lat, 5);

        driver.Lat = 21.02980;
        driver.Lng = 105.85360;
        driver.Status = DriverStatus.OnTrip;
        await db.SaveChangesAsync();
        await DatabaseSeeder.SeedAsync(db);
        Assert.Equal(21.02980, driver.Lat, 5);
        Assert.Equal(105.85360, driver.Lng, 5);

        driver.Status = DriverStatus.Online;
        await db.SaveChangesAsync();
        await DatabaseSeeder.SeedAsync(db);
        Assert.Equal(21.0352316, driver.Lat, 5);
        Assert.Equal(105.8209560, driver.Lng, 5);

        driver.Lat = 21.04000;
        driver.Lng = 105.84000;
        await db.SaveChangesAsync();
        await DatabaseSeeder.SeedAsync(db);
        Assert.Equal(21.04000, driver.Lat, 5);
        Assert.Equal(105.84000, driver.Lng, 5);
    }

    [Fact]
    public async Task Failed_notifications_do_not_turn_a_committed_create_into_an_HTTP_failure()
    {
        var publisher = new FailingPublisher();
        await using var fixture = new ApiFactory(Database, publisher);

        var rideId = await CreateRide(fixture.Client(An));

        Assert.True(publisher.Attempts > 0);
        var current = await Read(await fixture.Client(An).GetAsync("/api/rides/current"), HttpStatusCode.OK);
        Assert.Equal(rideId, current.GetProperty("rideId").GetGuid());
        Assert.Equal("Searching", current.GetProperty("status").GetString());
        Assert.NotEqual(Guid.Empty, await PendingOffer(fixture.Client(Khoa), rideId));
    }

    [Fact]
    public async Task Failed_notifications_do_not_turn_a_committed_accept_into_an_HTTP_failure()
    {
        var publisher = new FailingPublisher();
        await using var fixture = new ApiFactory(Database, publisher);
        var rideId = await CreateRide(fixture.Client(An));
        var offerId = await PendingOffer(fixture.Client(Khoa), rideId);
        var attemptsBeforeAccept = publisher.Attempts;

        var accepted = await Read(
            await fixture.Client(Khoa).PostAsync($"/api/offers/{offerId}/accept", content: null),
            HttpStatusCode.OK);

        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());
        Assert.True(publisher.Attempts > attemptsBeforeAccept);
        var current = await Read(await fixture.Client(Khoa).GetAsync("/api/rides/current"), HttpStatusCode.OK);
        Assert.Equal(rideId, current.GetProperty("rideId").GetGuid());
        Assert.Equal("Accepted", current.GetProperty("status").GetString());
        Assert.Equal(Guid.Parse(Khoa), current.GetProperty("driver").GetProperty("userId").GetGuid());
        Assert.Equal(Guid.Empty, await PendingOffer(fixture.Client(Linh), rideId));
    }

    [Fact]
    public async Task Dispatch_expiry_advances_rounds_without_running_driver_simulation()
    {
        await using var fixture = new ApiFactory(Database);
        var rideId = await CreateRide(fixture.Client(An));
        var firstExpiry = DateTimeOffset.UtcNow.AddSeconds(31);

        // Separate scopes model the independently scheduled dispatch job.
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var expiry = scope.ServiceProvider.GetRequiredService<IDispatchExpiryService>();
            var events = await expiry.AdvanceAsync(firstExpiry);
            Assert.Single(events.OfType<OutboundEvent.OfferClosed>());
        }

        var roundTwo = await Read(await fixture.Client(An).GetAsync($"/api/rides/{rideId}"), HttpStatusCode.OK);
        Assert.Equal("Searching", roundTwo.GetProperty("status").GetString());
        Assert.Equal(2, roundTwo.GetProperty("round").GetInt32());
        Assert.NotEqual(Guid.Empty, await PendingOffer(fixture.Client(Linh), rideId));
        Assert.Equal(Guid.Empty, await PendingOffer(fixture.Client(Khoa), rideId));
        Assert.Equal(Guid.Empty, await PendingOffer(fixture.Client(Ha), rideId));

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var expiry = scope.ServiceProvider.GetRequiredService<IDispatchExpiryService>();
            await expiry.AdvanceAsync(firstExpiry.AddSeconds(31));
            await expiry.AdvanceAsync(firstExpiry.AddSeconds(62));
            await expiry.AdvanceAsync(firstExpiry.AddSeconds(93));
        }

        var exhausted = await Read(await fixture.Client(An).GetAsync($"/api/rides/{rideId}"), HttpStatusCode.OK);
        Assert.Equal("NoDriver", exhausted.GetProperty("status").GetString());
    }

    private static async Task<Guid> CreateRide(HttpClient rider)
    {
        var quote = await Read(await rider.PostAsJsonAsync("/api/quotes", new
        {
            pickup = BenThanh,
            dropoff = Bitexco,
        }), HttpStatusCode.Created);
        var ride = await Read(await rider.PostAsJsonAsync("/api/rides", new
        {
            quoteId = quote.GetProperty("quoteId").GetGuid(),
        }), HttpStatusCode.Created);
        return ride.GetProperty("rideId").GetGuid();
    }

    private static async Task<Guid> PendingOffer(HttpClient driver, Guid rideId)
    {
        var offers = await Read(await driver.GetAsync("/api/offers/pending"), HttpStatusCode.OK);
        foreach (var offer in offers.EnumerateArray())
        {
            if (offer.GetProperty("rideId").GetGuid() == rideId)
                return offer.GetProperty("offerId").GetGuid();
        }

        return Guid.Empty;
    }

    private static async Task<string> StatusAfter(HttpClient client, string path)
    {
        var body = await Read(await client.PostAsync(path, content: null), HttpStatusCode.OK);
        return body.GetProperty("status").GetString() ?? "";
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode} {response.StatusCode}: {text}");
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "null" : text);
        return document.RootElement.Clone();
    }

    private static (double Lng, double Lat) Point(JsonElement coordinate)
    {
        var values = coordinate.EnumerateArray().ToList();
        return (values[0].GetDouble(), values[1].GetDouble());
    }

    private static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusM = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180d;
        var dLng = (lng2 - lng1) * Math.PI / 180d;
        var lat1R = lat1 * Math.PI / 180d;
        var lat2R = lat2 * Math.PI / 180d;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1R) * Math.Cos(lat2R) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return earthRadiusM * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private sealed record Pin(double Lat, double Lng, string Address);

    private sealed class GatedPickupRouter(int expected) : IPickupRouter
    {
        private int _entered;
        private readonly TaskCompletionSource _allEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task AllEntered => _allEntered.Task;

        public void Release() => _release.TrySetResult();

        public async Task<GeoRoute> ToPickupAsync(double lat, double lng, GeoPoint pickup, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _entered) >= expected)
                _allEntered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return PickupRoutes.Straight(lat, lng, pickup.Lat, pickup.Lng);
        }
    }

    private sealed class FailingPublisher : IRideEventPublisher
    {
        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);

        public Task PublishAsync(IReadOnlyList<OutboundEvent> events, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);
            throw new InvalidOperationException("Simulated SignalR outage");
        }
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _adminConnection;
        private readonly string _databaseName;
        private readonly string _connectionString;
        private readonly IRideEventPublisher? _publisher;
        private readonly IPickupRouter? _pickupRouter;
        private readonly string? _environment;

        public ApiFactory(string adminConnection, IRideEventPublisher? publisher = null, string? environment = null, IPickupRouter? pickupRouter = null)
        {
            _adminConnection = adminConnection;
            _publisher = publisher;
            _environment = environment;
            _pickupRouter = pickupRouter;
            _databaseName = "hr" + Guid.NewGuid().ToString("N");
            _connectionString = new Npgsql.NpgsqlConnectionStringBuilder(adminConnection) { Database = _databaseName }.ConnectionString;
            using var connection = new Npgsql.NpgsqlConnection(adminConnection);
            connection.Open();
            using var command = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            command.ExecuteNonQuery();
        }

        public HttpClient Client(string userId)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-User-Id", userId);
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            if (_environment is not null)
                builder.UseEnvironment(_environment);
            builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", "");
            builder.UseSetting("ConnectionStrings:GrabBike", _connectionString);
            builder.UseSetting("Dispatch:Bus", "InMemory");
            if (_publisher is not null || _pickupRouter is not null)
            {
                builder.ConfigureServices(services =>
                {
                    if (_publisher is not null)
                    {
                        services.RemoveAll<IRideEventPublisher>();
                        services.AddSingleton(_publisher);
                    }
                    if (_pickupRouter is not null)
                    {
                        services.RemoveAll<IPickupRouter>();
                        services.AddSingleton(_pickupRouter);
                    }
                });
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try
            {
                using var connection = new Npgsql.NpgsqlConnection(_adminConnection);
                connection.Open();
                using (var terminate = new Npgsql.NpgsqlCommand(
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()",
                    connection))
                {
                    terminate.Parameters.AddWithValue("name", _databaseName);
                    terminate.ExecuteNonQuery();
                }
                using var drop = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\"", connection);
                drop.ExecuteNonQuery();
            }
            catch (Npgsql.NpgsqlException)
            {
            }
        }
    }
}
