using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Maps;
using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.Infrastructure.Persistence;
using GrabBike.Api.Infrastructure.Persistence.Repositories;
using GrabBike.Api.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace GrabBike.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class BusinessLoggingTests(PostgresFixture postgres)
{
    private static readonly Guid RiderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DriverId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-20T12:00:00Z");

    [Fact]
    public async Task Booking_acceptance_and_completion_logs_describe_already_persisted_state()
    {
        await using var fixture = await Fixture.CreateAsync(postgres.Container.GetConnectionString());
        fixture.Logs.Observe = fixture.AssertCommitted;
        var rideId = await fixture.BookAsync();
        var offerId = await fixture.OfferForDriverAsync();

        await fixture.Offers.AcceptAsync(DriverId, offerId, Now);
        await fixture.Rides.ArrivedAsync(DriverId, rideId);
        await fixture.Rides.StartAsync(DriverId, rideId);
        await fixture.Rides.DropoffAsync(DriverId, rideId);
        await fixture.Rides.ConfirmArrivalAsync(RiderId, rideId);

        var booked = Assert.Single(fixture.Logs.Entries, entry => entry.Event.Name == "RideBooked");
        Assert.Equal(rideId, booked.Fields["RideId"]);
        Assert.Equal("Searching", booked.Fields["Status"]);
        var accepted = Assert.Single(fixture.Logs.Entries, entry => entry.Event.Name == "OfferAccepted");
        Assert.Equal(offerId, accepted.Fields["OfferId"]);
        Assert.Equal(DriverId, accepted.Fields["DriverId"]);
        Assert.Equal(new[] { "DriverArrived", "InProgress", "DroppedOff", "Completed" },
            fixture.Logs.Entries.Where(entry => entry.Event.Name == "RideStatusChanged")
                .Select(entry => (string)entry.Fields["Status"]!).ToArray());
        Assert.All(fixture.Logs.Entries, entry => Assert.Equal(LogLevel.Information, entry.Level));
        AssertPrivatePayloadsExcluded(fixture.Logs.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_logs_previous_status_and_committed_offer_closures(bool accepted)
    {
        await using var fixture = await Fixture.CreateAsync(postgres.Container.GetConnectionString());
        var rideId = await fixture.BookAsync();
        var offerId = await fixture.OfferForDriverAsync();
        if (accepted) await fixture.Offers.AcceptAsync(DriverId, offerId, Now);
        fixture.Logs.Entries.Clear();
        fixture.Logs.Observe = fixture.AssertCommitted;

        await fixture.Rides.CancelAsync(RiderId, rideId);

        var cancelled = Assert.Single(fixture.Logs.Entries, entry => entry.Event.Name == "RideStatusChanged");
        Assert.Equal(rideId, cancelled.Fields["RideId"]);
        Assert.Equal(accepted ? "Accepted" : "Searching", cancelled.Fields["FromStatus"]);
        Assert.Equal("Cancelled", cancelled.Fields["Status"]);
        Assert.Equal(accepted ? 1 : 2, fixture.Logs.Entries.Count);
        if (accepted)
        {
            Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Event.Name == "DispatchOfferClosed");
        }
        else
        {
            var closed = Assert.Single(fixture.Logs.Entries, entry => entry.Event.Name == "DispatchOfferClosed");
            Assert.Equal(rideId, closed.Fields["RideId"]);
            Assert.Equal(offerId, closed.Fields["OfferId"]);
            Assert.Equal(DriverId, closed.Fields["DriverId"]);
            Assert.Equal("Cancelled", closed.Fields["Reason"]);
        }
    }

    [Fact]
    public async Task Declining_an_offer_logs_once_and_idempotent_retries_are_quiet()
    {
        await using var fixture = await Fixture.CreateAsync(postgres.Container.GetConnectionString());
        var rideId = await fixture.BookAsync();
        var offerId = await fixture.OfferForDriverAsync();
        fixture.Logs.Entries.Clear();
        fixture.Logs.Observe = fixture.AssertCommitted;

        await fixture.Offers.DeclineAsync(DriverId, offerId, Now);

        Assert.Equal(3, fixture.Logs.Entries.Count);
        var declined = Assert.Single(fixture.Logs.Entries, entry => entry.Event.Name == "OfferDeclined");
        Assert.Equal(rideId, declined.Fields["RideId"]);
        Assert.Equal(offerId, declined.Fields["OfferId"]);
        Assert.Equal(DriverId, declined.Fields["DriverId"]);
        var replacement = Assert.Single(fixture.Logs.Entries, entry => entry.Event.Name == "DispatchOfferCreated");
        Assert.Equal(rideId, replacement.Fields["RideId"]);
        Assert.NotEqual(offerId, Assert.IsType<Guid>(replacement.Fields["OfferId"]));
        Assert.NotEqual(DriverId, Assert.IsType<Guid>(replacement.Fields["DriverId"]));
        Assert.Equal(2, replacement.Fields["Round"]);
        var round = Assert.Single(fixture.Logs.Entries, entry => entry.Event.Name == "DispatchRoundStarted");
        Assert.Equal(rideId, round.Fields["RideId"]);
        Assert.Equal(2, round.Fields["Round"]);

        fixture.Logs.Entries.Clear();
        await fixture.Offers.DeclineAsync(DriverId, offerId, Now);
        Assert.Empty(fixture.Logs.Entries);
    }

    [Fact]
    public async Task Expiry_logs_committed_rounds_and_exhaustion_but_idle_ticks_are_quiet()
    {
        await using var fixture = await Fixture.CreateAsync(postgres.Container.GetConnectionString());
        HashSet<Guid> eligibleDriverIds;
        using (var seeded = fixture.ReadContext())
        {
            var pickup = seeded.Quotes.AsNoTracking().Single(quote => quote.Id == fixture.QuoteId).Pickup;
            eligibleDriverIds = seeded.Drivers.AsNoTracking().ToList()
                .Where(driver => driver.Status == DriverStatus.Online && driver.CurrentRideId is null
                    && Geo.HaversineMeters(pickup.Lat, pickup.Lng, driver.Lat, driver.Lng) <= 3000)
                .Select(driver => driver.UserId).ToHashSet();
            Assert.Equal(4, eligibleDriverIds.Count);
        }
        var rideId = await fixture.BookAsync();
        var currentOfferId = await fixture.OfferForDriverAsync();
        var currentDriverId = DriverId;
        HashSet<Guid> offeredIds = [currentOfferId];
        HashSet<Guid> offeredDriverIds = [currentDriverId];
        fixture.Logs.Entries.Clear();
        fixture.Logs.Observe = fixture.AssertCommitted;

        await fixture.Expiry.AdvanceAsync(Now.AddSeconds(1));
        Assert.Empty(fixture.Logs.Entries);
        foreach (var (seconds, expectedRound) in new[] { (31, 2), (62, 3), (93, 4) })
        {
            var before = fixture.Logs.Entries.Count;
            await fixture.Expiry.AdvanceAsync(Now.AddSeconds(seconds));
            var emitted = fixture.Logs.Entries.Skip(before).ToArray();
            Assert.Equal(3, emitted.Length);
            var closed = Assert.Single(emitted, entry => entry.Event.Name == "DispatchOfferClosed");
            Assert.Equal(rideId, closed.Fields["RideId"]);
            Assert.Equal(currentOfferId, closed.Fields["OfferId"]);
            Assert.Equal(currentDriverId, closed.Fields["DriverId"]);
            Assert.Equal("Expired", closed.Fields["Reason"]);

            var replacement = Assert.Single(emitted, entry => entry.Event.Name == "DispatchOfferCreated");
            Assert.Equal(rideId, replacement.Fields["RideId"]);
            Assert.Equal(expectedRound, replacement.Fields["Round"]);
            currentOfferId = Assert.IsType<Guid>(replacement.Fields["OfferId"]);
            currentDriverId = Assert.IsType<Guid>(replacement.Fields["DriverId"]);
            Assert.True(offeredIds.Add(currentOfferId), "Each replacement must have a new offer ID.");
            Assert.True(offeredDriverIds.Add(currentDriverId), "Each replacement must try a different driver.");
            Assert.Contains(currentDriverId, eligibleDriverIds);
            var round = Assert.Single(emitted, entry => entry.Event.Name == "DispatchRoundStarted");
            Assert.Equal(rideId, round.Fields["RideId"]);
            Assert.Equal(expectedRound, round.Fields["Round"]);
            Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Event.Name == "DispatchExhausted");
        }

        var beforeExhaustion = fixture.Logs.Entries.Count;
        await fixture.Expiry.AdvanceAsync(Now.AddSeconds(124));
        var finalEvents = fixture.Logs.Entries.Skip(beforeExhaustion).ToArray();
        Assert.Equal(2, finalEvents.Length);
        var finalClosed = Assert.Single(finalEvents, entry => entry.Event.Name == "DispatchOfferClosed");
        Assert.Equal(currentOfferId, finalClosed.Fields["OfferId"]);
        Assert.Equal(currentDriverId, finalClosed.Fields["DriverId"]);
        var exhausted = Assert.Single(fixture.Logs.Entries, entry => entry.Event.Name == "DispatchExhausted");
        Assert.Equal(rideId, exhausted.Fields["RideId"]);
        Assert.Equal("NoDriver", exhausted.Fields["Status"]);
        Assert.Equal(4, exhausted.Fields["Round"]);
        var closures = fixture.Logs.Entries.Where(entry => entry.Event.Name == "DispatchOfferClosed").ToArray();
        Assert.Equal(4, closures.Length);
        Assert.Equal(4, closures.Select(entry => (Guid)entry.Fields["OfferId"]!).Distinct().Count());
        Assert.True(eligibleDriverIds.SetEquals(closures.Select(entry => (Guid)entry.Fields["DriverId"]!)));
        Assert.All(closures, entry =>
        {
            Assert.Equal(rideId, entry.Fields["RideId"]);
            Assert.Equal("Expired", entry.Fields["Reason"]);
        });
        using (var persisted = fixture.ReadContext())
        {
            var offers = persisted.Offers.AsNoTracking().Where(offer => offer.RideId == rideId).ToArray();
            Assert.Equal(4, offers.Length);
            Assert.All(offers, offer => Assert.Equal(OfferStatus.Closed, offer.Status));
            Assert.DoesNotContain(offers, offer => offer.Status == OfferStatus.Pending);
            var ride = persisted.Rides.AsNoTracking().Single(ride => ride.Id == rideId);
            Assert.Equal(RideStatus.NoDriver, ride.Status);
            Assert.Equal(4, ride.Round);
        }
        var count = fixture.Logs.Entries.Count;
        await fixture.Expiry.AdvanceAsync(Now.AddSeconds(125));
        Assert.Equal(count, fixture.Logs.Entries.Count);
    }

    [Theory]
    [InlineData("book")]
    [InlineData("accept")]
    [InlineData("decline")]
    [InlineData("arrived")]
    [InlineData("cancel")]
    [InlineData("complete")]
    [InlineData("expire")]
    public async Task Failed_save_never_emits_business_success(string operation)
    {
        await using var fixture = await Fixture.CreateAsync(postgres.Container.GetConnectionString());
        var rideId = operation == "book" ? Guid.Empty : await fixture.BookAsync();
        var offerId = rideId == Guid.Empty ? Guid.Empty : await fixture.OfferForDriverAsync();
        if (operation is "arrived" or "complete")
            await fixture.Offers.AcceptAsync(DriverId, offerId, Now);
        if (operation == "complete")
        {
            await fixture.Rides.ArrivedAsync(DriverId, rideId);
            await fixture.Rides.StartAsync(DriverId, rideId);
            await fixture.Rides.DropoffAsync(DriverId, rideId);
        }
        using var beforeDb = fixture.ReadContext();
        var beforeStatus = beforeDb.Rides.AsNoTracking().SingleOrDefault()?.Status;
        fixture.Logs.Entries.Clear();
        fixture.UnitOfWork.Fail = true;

        Task Act() => operation switch
        {
            "book" => fixture.Rides.CreateAsync(RiderId, fixture.QuoteId, Now),
            "accept" => fixture.Offers.AcceptAsync(DriverId, offerId, Now),
            "decline" => fixture.Offers.DeclineAsync(DriverId, offerId, Now),
            "arrived" => fixture.Rides.ArrivedAsync(DriverId, rideId),
            "cancel" => fixture.Rides.CancelAsync(RiderId, rideId),
            "complete" => fixture.Rides.ConfirmArrivalAsync(RiderId, rideId),
            "expire" => fixture.Expiry.AdvanceAsync(Now.AddSeconds(31)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        await Assert.ThrowsAsync<InvalidOperationException>(Act);

        Assert.Empty(fixture.Logs.Entries);
        using var afterDb = fixture.ReadContext();
        Assert.Equal(beforeStatus, afterDb.Rides.AsNoTracking().SingleOrDefault()?.Status);
    }

    [Fact]
    public async Task Expected_rejections_emit_neither_success_nor_error_logs()
    {
        await using var fixture = await Fixture.CreateAsync(postgres.Container.GetConnectionString());
        var rideId = await fixture.BookAsync();
        var offerId = await fixture.OfferForDriverAsync();
        fixture.Logs.Entries.Clear();

        Assert.IsType<RideOutcome.NotFound>(await fixture.Rides.CreateAsync(RiderId, Guid.NewGuid(), Now));
        Assert.IsType<RideOutcome.ActiveRideConflict>(await fixture.Rides.CreateAsync(RiderId, fixture.QuoteId, Now));
        Assert.IsType<RideOutcome.Forbidden>(await fixture.Rides.CancelAsync(Guid.NewGuid(), rideId));
        Assert.IsType<RideOutcome.TransitionConflict>(await fixture.Rides.ConfirmArrivalAsync(RiderId, rideId));
        Assert.IsType<OfferOutcome.Forbidden>(await fixture.Offers.AcceptAsync(Guid.NewGuid(), offerId, Now));
        Assert.Empty(fixture.Logs.Entries);

        await fixture.Offers.AcceptAsync(DriverId, offerId, Now);
        fixture.Logs.Entries.Clear();
        Assert.IsType<OfferOutcome.Conflict>(await fixture.Offers.AcceptAsync(DriverId, offerId, Now));
        Assert.Empty(fixture.Logs.Entries);
    }

    [Fact]
    public async Task Notification_failures_include_identifiers_and_exception_without_payloads()
    {
        await using var fixture = await Fixture.CreateAsync(postgres.Container.GetConnectionString());
        var created = Assert.IsType<RideOutcome.Created>(await fixture.Rides.CreateAsync(RiderId, fixture.QuoteId, Now));
        var received = created.Events.OfType<OutboundEvent.OfferReceived>().First();
        var changed = created.Events.OfType<OutboundEvent.StatusChanged>().Single();
        var exception = new InvalidOperationException("Simulated transport failure");
        var logs = new CapturedLogs();
        var delivery = new NotificationDelivery(new FailingPublisher(exception), logs.Logger<NotificationDelivery>());
        OutboundEvent[] events = [received, changed,
            new OutboundEvent.OfferClosed(received.DriverId, received.Offer.OfferId, created.Body.RideId, "Taken"),
            new OutboundEvent.Location(RiderId, DriverId,
                new DriverLocationDto(created.Body.RideId, DriverId, 10.77254, 106.698, 20, 30, Now))];

        await delivery.DeliverAsync(events);

        Assert.Equal(events.Length, logs.Entries.Count);
        Assert.All(logs.Entries, entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Equal("NotificationDeliveryFailed", entry.Event.Name);
            Assert.Same(exception, entry.Exception);
            Assert.Equal(created.Body.RideId, entry.Fields["RideId"]);
        });
        Assert.Equal(received.Offer.OfferId, logs.Entries[0].Fields["OfferId"]);
        Assert.Equal(received.DriverId, logs.Entries[0].Fields["DriverId"]);
        Assert.Equal(received.Offer.OfferId, logs.Entries[2].Fields["OfferId"]);
        AssertPrivatePayloadsExcluded(logs.Entries);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_logged_as_a_notification_outage()
    {
        var logs = new CapturedLogs();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var delivery = new NotificationDelivery(
            new FailingPublisher(new OperationCanceledException(cancellation.Token)), logs.Logger<NotificationDelivery>());

        await delivery.DeliverAsync([new OutboundEvent.OfferClosed(DriverId, Guid.NewGuid(), Guid.NewGuid(), "Expired")],
            cancellation.Token);

        Assert.Empty(logs.Entries);
    }

    private static void AssertPrivatePayloadsExcluded(IEnumerable<LogEntry> entries)
    {
        string[] allowed = ["RideId", "OfferId", "DriverId", "Status", "FromStatus", "Round", "Reason", "EventType", "{OriginalFormat}"];
        Assert.All(entries, entry =>
        {
            Assert.All(entry.Fields.Keys, key => Assert.Contains(key, allowed));
            Assert.DoesNotContain("Private pickup address", entry.Message);
            Assert.DoesNotContain("Private dropoff address", entry.Message);
            Assert.DoesNotContain("10.77254", entry.Message);
            Assert.DoesNotContain("106.698", entry.Message);
        });
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _adminConnection;
        private readonly string _databaseName;
        private readonly string _connectionString;
        private DbContextOptions<AppDbContext> _options = null!;
        private AppDbContext _db = null!;
        public CapturedLogs Logs { get; } = new();
        public SavingUnitOfWork UnitOfWork { get; private set; } = null!;
        public RideService Rides { get; private set; } = null!;
        public OfferService Offers { get; private set; } = null!;
        public DispatchExpiryService Expiry { get; private set; } = null!;
        public Guid QuoteId { get; } = Guid.NewGuid();

        private Fixture(string adminConnection)
        {
            _adminConnection = adminConnection;
            _databaseName = "hr" + Guid.NewGuid().ToString("N");
            _connectionString = new NpgsqlConnectionStringBuilder(adminConnection) { Database = _databaseName }.ConnectionString;
        }

        public static async Task<Fixture> CreateAsync(string adminConnection)
        {
            var fixture = new Fixture(adminConnection);
            await using (var admin = new NpgsqlConnection(fixture._adminConnection))
            {
                await admin.OpenAsync();
                await using var create = new NpgsqlCommand($"CREATE DATABASE \"{fixture._databaseName}\"", admin);
                await create.ExecuteNonQueryAsync();
            }

            await using (var database = new NpgsqlConnection(fixture._connectionString))
            {
                await database.OpenAsync();
                await using var extension = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS postgis", database);
                await extension.ExecuteNonQueryAsync();
            }

            fixture._options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(fixture._connectionString, npgsql => npgsql.UseNetTopologySuite())
                .Options;
            fixture._db = new AppDbContext(fixture._options);
            await fixture._db.Database.EnsureCreatedAsync();
            await DatabaseSeeder.SeedAsync(fixture._db);
            fixture._db.Quotes.Add(new Quote
            {
                Id = fixture.QuoteId, Provider = "Fake", DistanceM = 1000, DurationS = 180, FareVnd = 12000,
                Pickup = new GeoPoint { Lat = 10.77254, Lng = 106.698, Address = "Private pickup address" },
                Dropoff = new GeoPoint { Lat = 10.77175, Lng = 106.70446, Address = "Private dropoff address" },
                Route = new GeoRoute("LineString", [[106.698, 10.77254], [106.70446, 10.77175]]),
            });
            await fixture._db.SaveChangesAsync();
            var rides = new RideRepository(fixture._db);
            var offers = new OfferRepository(fixture._db);
            var drivers = new DriverRepository(fixture._db);
            var dispatch = new DispatchService(offers, drivers);
            var writeLock = new RideWriteLock();
            var pickupRoutes = new PickupRoutes();
            fixture.UnitOfWork = new SavingUnitOfWork(fixture._db);
            var bus = new InlineDispatchBus(async (message, cancellationToken) =>
            {
                if (message is not DispatchMessage.Requested requested)
                    return [];
                var ride = await rides.FindAsync(requested.RideId, cancellationToken);
                if (ride is null)
                    return [];
                var events = new List<OutboundEvent>();
                await dispatch.AdvanceAsync(ride, Now, events, cancellationToken);
                await fixture.UnitOfWork.SaveChangesAsync(cancellationToken);
                return events;
            });
            fixture.Rides = new RideService(new QuoteRepository(fixture._db), rides, offers, drivers, bus,
                fixture.UnitOfWork, writeLock, pickupRoutes, new DriverRoam(), fixture.Logs.Logger<RideService>());
            fixture.Offers = new OfferService(offers, rides, drivers, dispatch, bus, fixture.UnitOfWork, writeLock,
                new StraightPickupRouter(), pickupRoutes, fixture.Logs.Logger<OfferService>());
            fixture.Expiry = new DispatchExpiryService(offers, rides, dispatch, fixture.UnitOfWork, writeLock,
                fixture.Logs.Logger<DispatchExpiryService>());
            return fixture;
        }

        public async Task<Guid> BookAsync() =>
            Assert.IsType<RideOutcome.Created>(await Rides.CreateAsync(RiderId, QuoteId, Now)).Body.RideId;

        public Task<Guid> OfferForDriverAsync() =>
            _db.Offers.Where(offer => offer.DriverId == DriverId).Select(offer => offer.Id).SingleAsync();

        public AppDbContext ReadContext() => new(_options);

        public void AssertCommitted(LogEntry entry)
        {
            // Read with a separate context during emission, not from tracked
            // entities that may have changed before SaveChanges succeeds.
            using var persisted = ReadContext();
            var rideId = (Guid)entry.Fields["RideId"]!;
            var ride = persisted.Rides.AsNoTracking().Single(ride => ride.Id == rideId);
            if (entry.Fields.TryGetValue("Status", out var status))
                Assert.Equal(status, ride.Status.ToString());
            if (entry.Fields.TryGetValue("Round", out var round))
                Assert.Equal(round, ride.Round);
            if (entry.Fields.TryGetValue("OfferId", out var id))
            {
                var offerId = (Guid)id!;
                var offer = persisted.Offers.AsNoTracking().Single(offer => offer.Id == offerId);
                Assert.Equal(entry.Fields["DriverId"], offer.DriverId);
                var expected = entry.Event.Name switch
                {
                    "OfferAccepted" => OfferStatus.Accepted,
                    "OfferDeclined" => OfferStatus.Declined,
                    "DispatchOfferClosed" => OfferStatus.Closed,
                    _ => OfferStatus.Pending,
                };
                Assert.Equal(expected, offer.Status);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            try
            {
                await using var admin = new NpgsqlConnection(_adminConnection);
                await admin.OpenAsync();
                await using (var terminate = new NpgsqlCommand(
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()",
                    admin))
                {
                    terminate.Parameters.AddWithValue("name", _databaseName);
                    await terminate.ExecuteNonQueryAsync();
                }

                await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\"", admin);
                await drop.ExecuteNonQueryAsync();
            }
            catch (NpgsqlException)
            {
            }
        }
    }

    private sealed class InlineDispatchBus(Func<DispatchMessage, CancellationToken, Task<IReadOnlyList<OutboundEvent>>> publish) : IDispatchBus
    {
        public string Mode => "InMemory";

        public Task<IReadOnlyList<OutboundEvent>> PublishAsync(DispatchMessage message, CancellationToken cancellationToken = default) =>
            publish(message, cancellationToken);
    }

    private sealed class SavingUnitOfWork(AppDbContext db) : IUnitOfWork
    {
        public bool Fail { get; set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Fail
            ? throw new InvalidOperationException("Simulated save failure")
            : db.SaveChangesAsync(cancellationToken);
    }

    private sealed record LogEntry(LogLevel Level, EventId Event, string Message, Exception? Exception,
        IReadOnlyDictionary<string, object?> Fields);

    private sealed class CapturedLogs
    {
        public List<LogEntry> Entries { get; } = [];
        public Action<LogEntry>? Observe { get; set; }
        public ILogger<T> Logger<T>() => new CaptureLogger<T>(this);

        private sealed class CaptureLogger<T>(CapturedLogs owner) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var fields = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(item => item.Key, item => item.Value);
                var entry = new LogEntry(logLevel, eventId, formatter(state, exception), exception, fields);
                owner.Observe?.Invoke(entry);
                owner.Entries.Add(entry);
            }
        }
    }

    private sealed class StraightPickupRouter : IPickupRouter
    {
        public Task<GeoRoute> ToPickupAsync(double lat, double lng, GeoPoint pickup, CancellationToken cancellationToken = default) =>
            Task.FromResult(PickupRoutes.Straight(lat, lng, pickup.Lat, pickup.Lng));
    }

    private sealed class FailingPublisher(Exception exception) : IRideEventPublisher
    {
        public Task PublishAsync(IReadOnlyList<OutboundEvent> events, CancellationToken cancellationToken = default) =>
            Task.FromException(exception);
    }
}
