using Microsoft.AspNetCore.SignalR;

namespace GrabBike.Api;

public sealed class RideHub : Hub;

public sealed class QueryUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        var raw = connection.GetHttpContext()?.Request.Query["userId"].ToString();
        return Guid.TryParse(raw, out var id) ? id.ToString("D") : null;
    }
}

public sealed class RideNotifier(IHubContext<RideHub> hub)
{
    public async Task PublishAsync(IReadOnlyList<OutboundEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var outbound in events)
        {
            switch (outbound)
            {
                case OutboundEvent.OfferReceived received:
                    await hub.Clients.User(Format(received.DriverId))
                        .SendAsync("OfferReceived", received.Offer, cancellationToken);
                    break;
                case OutboundEvent.OfferClosed closed:
                    await hub.Clients.User(Format(closed.DriverId))
                        .SendAsync("OfferClosed", new OfferClosedDto(closed.OfferId, closed.Reason), cancellationToken);
                    break;
                case OutboundEvent.StatusChanged changed:
                    var recipients = new List<string> { Format(changed.RiderId) };
                    if (changed.DriverId is Guid driverId)
                        recipients.Add(Format(driverId));
                    await hub.Clients.Users(recipients)
                        .SendAsync("RideStatusChanged", changed.View, cancellationToken);
                    break;
                case OutboundEvent.Location location:
                    await hub.Clients.Users([Format(location.RiderId), Format(location.DriverId)])
                        .SendAsync("DriverLocation", location.Payload, cancellationToken);
                    break;
            }
        }
    }

    private static string Format(Guid id) => id.ToString("D");
}

public sealed class DriverMovementService(
    RideStore store,
    RideNotifier notifier,
    ILogger<DriverMovementService> logger) : BackgroundService
{
    public const double StepMeters = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                List<OutboundEvent> events;
                try
                {
                    events = store.Tick(DateTimeOffset.UtcNow, StepMeters);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Driver movement tick failed");
                    continue;
                }

                try
                {
                    await notifier.PublishAsync(events, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ride broadcast failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
