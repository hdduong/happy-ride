using System.Net;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.Infrastructure.Persistence;
using GrabBike.Api.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GrabBike.Api.Tests;

public sealed partial class ObservabilityTests
{
    [Fact]
    public async Task Correlation_reads_cannot_make_a_queued_cancel_use_pre_start_ride_state()
    {
        await using var factory = new ObservabilityFactory(postgres.Container.GetConnectionString());
        var commandLock = new OrderedCommandLock();
        await using var gatedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRideWriteLock>();
            services.AddSingleton<IRideWriteLock>(commandLock);
        }));
        using var client = gatedFactory.CreateClient();
        var rideId = Guid.NewGuid();
        await using (var scope = gatedFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Rides.Add(new Ride
            {
                Id = rideId, RiderId = Guid.Parse(Rider), DriverId = Guid.Parse(Driver),
                QuoteId = Guid.NewGuid(), Status = RideStatus.DriverArrived,
                Pickup = new GeoPoint { Lat = 10.77, Lng = 106.69 }, Dropoff = new GeoPoint { Lat = 10.78, Lng = 106.70 },
                DistanceM = 1000, DurationS = 300, FareVnd = 15000,
                Route = new GeoRoute("LineString", []), CreatedAt = DateTimeOffset.UtcNow.UtcTicks,
            });
            (await db.Drivers.FindAsync(Guid.Parse(Driver)))!.CurrentRideId = rideId;
            await db.SaveChangesAsync();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var start = new HttpRequestMessage(HttpMethod.Post, $"/api/rides/{rideId}/start");
        start.Headers.Add("X-User-Id", Driver);
        var starting = client.SendAsync(start, timeout.Token);
        await commandLock.FirstArrived.Task.WaitAsync(timeout.Token);

        // Both HTTP requests have completed their telemetry lookup before either command can read.
        using var cancel = new HttpRequestMessage(HttpMethod.Post, $"/api/rides/{rideId}/cancel");
        cancel.Headers.Add("X-User-Id", Rider);
        var cancelling = client.SendAsync(cancel, timeout.Token);
        await commandLock.SecondArrived.Task.WaitAsync(timeout.Token);
        commandLock.AllowFirst.TrySetResult();

        using var started = await starting;
        using var cancelled = await cancelling;
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancelled.StatusCode);
        await using var verificationScope = gatedFactory.Services.CreateAsyncScope();
        var saved = await verificationScope.ServiceProvider.GetRequiredService<AppDbContext>().Rides.FindAsync(rideId);
        Assert.Equal(RideStatus.InProgress, saved!.Status);
    }

    private sealed class OrderedCommandLock : IRideWriteLock
    {
        private int _calls;
        public TaskCompletionSource FirstArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource FirstReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                FirstArrived.TrySetResult();
                await AllowFirst.Task.WaitAsync(cancellationToken);
                return new Release(() => FirstReleased.TrySetResult());
            }
            Assert.Equal(2, call);
            SecondArrived.TrySetResult();
            await FirstReleased.Task.WaitAsync(cancellationToken);
            return new Release(() => { });
        }

        private sealed class Release(Action release) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
