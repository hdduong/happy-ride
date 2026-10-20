using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GrabBike.Api.Tests;

public class RideFlowTests
{
    private const string An = "11111111-1111-1111-1111-111111111111";
    private const string Khoa = "33333333-3333-3333-3333-333333333333";
    private const string Linh = "44444444-4444-4444-4444-444444444444";
    private const string Duc = "55555555-5555-5555-5555-555555555555";
    private const string Ha = "66666666-6666-6666-6666-666666666666";
    private const string Nam = "77777777-7777-7777-7777-777777777777";

    private static readonly Pin BenThanh = new(10.77254, 106.69800, "Chợ Bến Thành, District 1");
    private static readonly Pin Bitexco = new(10.77175, 106.70446, "Bitexco Financial Tower, 2 Hai Trieu, District 1");

    [Fact]
    public async Task Quote_for_Ben_Thanh_to_Bitexco_returns_distance_fare_and_route()
    {
        await using var fixture = new ApiFactory();
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
        await using var fixture = new ApiFactory();
        var rideId = await CreateRide(fixture.Client(An));

        var ride = await Read(await fixture.Client(An).GetAsync($"/api/rides/{rideId}"), HttpStatusCode.OK);
        Assert.Equal("Searching", ride.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, ride.GetProperty("driver").ValueKind);

        var khoaOffer = await PendingOffer(fixture.Client(Khoa), rideId);
        var linhOffer = await PendingOffer(fixture.Client(Linh), rideId);
        var ducOffer = await PendingOffer(fixture.Client(Duc), rideId);
        Assert.NotEqual(Guid.Empty, khoaOffer);
        Assert.NotEqual(Guid.Empty, linhOffer);
        Assert.NotEqual(Guid.Empty, ducOffer);
        Assert.Equal(Guid.Empty, await PendingOffer(fixture.Client(Ha), rideId));

        var offers = await Read(await fixture.Client(Khoa).GetAsync("/api/offers/pending"), HttpStatusCode.OK);
        var offer = offers.EnumerateArray().Single(item => item.GetProperty("rideId").GetGuid() == rideId);
        Assert.Equal(1, offer.GetProperty("round").GetInt32());
        Assert.True(offer.GetProperty("distanceM").GetInt32() > 0);
        Assert.True(offer.GetProperty("durationS").GetInt32() > 0);
        Assert.Equal("Chợ Bến Thành, District 1", offer.GetProperty("pickup").GetProperty("address").GetString());
        var ttl = offer.GetProperty("expiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow;
        Assert.InRange(ttl.TotalSeconds, 20, 35);
    }

    [Fact]
    public async Task Two_concurrent_accepts_yield_one_success_and_one_conflict()
    {
        await using var fixture = new ApiFactory();
        var rideId = await CreateRide(fixture.Client(An));
        var khoaOffer = await PendingOffer(fixture.Client(Khoa), rideId);
        var linhOffer = await PendingOffer(fixture.Client(Linh), rideId);

        var first = fixture.Client(Khoa).PostAsync($"/api/offers/{khoaOffer}/accept", content: null);
        var second = fixture.Client(Linh).PostAsync($"/api/offers/{linhOffer}/accept", content: null);
        var responses = await Task.WhenAll(first, second);

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var body = await Read(conflict, HttpStatusCode.Conflict);
        Assert.Equal("Taken", body.GetProperty("reason").GetString());

        var winner = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
        var view = await Read(winner, HttpStatusCode.OK);
        Assert.Equal("Accepted", view.GetProperty("status").GetString());
        var driverId = view.GetProperty("driver").GetProperty("userId").GetGuid();
        Assert.Contains(driverId, new[] { Guid.Parse(Khoa), Guid.Parse(Linh) });
    }

    [Fact]
    public async Task Happy_path_completes_and_returns_driver_online()
    {
        await using var fixture = new ApiFactory();
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
        await using var fixture = new ApiFactory();
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
    public async Task Ride_outside_every_cluster_becomes_NoDriver()
    {
        await using var fixture = new ApiFactory();
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
    }

    [Fact]
    public async Task Declining_the_first_round_offers_the_remaining_driver()
    {
        await using var fixture = new ApiFactory();
        var rideId = await CreateRide(fixture.Client(An));
        foreach (var driverId in new[] { Khoa, Linh, Duc })
        {
            var offerId = await PendingOffer(fixture.Client(driverId), rideId);
            Assert.NotEqual(Guid.Empty, offerId);
            await Read(await fixture.Client(driverId).PostAsync($"/api/offers/{offerId}/decline", content: null), HttpStatusCode.OK);
        }

        var haOffers = await Read(await fixture.Client(Ha).GetAsync("/api/offers/pending"), HttpStatusCode.OK);
        var haOffer = Assert.Single(haOffers.EnumerateArray(), item => item.GetProperty("rideId").GetGuid() == rideId);
        Assert.Equal(2, haOffer.GetProperty("round").GetInt32());

        var ride = await Read(await fixture.Client(An).GetAsync($"/api/rides/{rideId}"), HttpStatusCode.OK);
        Assert.Equal("Searching", ride.GetProperty("status").GetString());
        Assert.Equal(2, ride.GetProperty("round").GetInt32());
    }

    [Fact]
    public async Task Hanoi_rider_quote_is_near_Hoan_Kiem()
    {
        await using var fixture = new ApiFactory();
        var quote = await Read(await fixture.Client(Nam).PostAsJsonAsync("/api/quotes", new
        {
            pickup = new Pin(21.0287, 105.8524, "Hồ Hoàn Kiếm"),
            dropoff = new Pin(21.0300, 105.8540, "Nearby"),
        }), HttpStatusCode.Created);
        var created = await Read(await fixture.Client(Nam).PostAsJsonAsync("/api/rides", new
        {
            quoteId = quote.GetProperty("quoteId").GetGuid(),
        }), HttpStatusCode.Created);
        var rideId = created.GetProperty("rideId").GetGuid();

        var ride = await Read(await fixture.Client(Nam).GetAsync($"/api/rides/{rideId}"), HttpStatusCode.OK);
        Assert.Equal("Searching", ride.GetProperty("status").GetString());
        Assert.NotEqual(Guid.Empty, await PendingOffer(fixture.Client("99999999-9999-9999-9999-999999999999"), rideId));
        Assert.Equal(Guid.Empty, await PendingOffer(fixture.Client(Khoa), rideId));
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

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        public HttpClient Client(string userId)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-User-Id", userId);
            return client;
        }
    }
}
