namespace GrabBike.Api.Features.Realtime;

public interface IRideNotifications
{
    Task DeliverAsync(IReadOnlyList<OutboundEvent> events, CancellationToken cancellationToken = default);
}

/// <summary>
/// Best-effort notifications after persistence succeeds. Clients reconcile through polling.
/// This is intentionally not a durable outbox; a failed delivery is logged, never rolled back.
/// </summary>
public sealed class NotificationDelivery(IRideEventPublisher publisher, ILogger<NotificationDelivery> logger) : IRideNotifications
{
    public async Task DeliverAsync(IReadOnlyList<OutboundEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var outbound in events)
        {
            try
            {
                await publisher.PublishAsync([outbound], cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Committed {EventType} notification was not delivered; clients will reconcile by polling", outbound.GetType().Name);
            }
        }
    }
}
