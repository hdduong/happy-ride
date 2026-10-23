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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal request/worker cancellation is not a delivery outage.
                return;
            }
            catch (Exception ex)
            {
                var (rideId, offerId, driverId) = Correlation(outbound);
                logger.LogWarning(new EventId(2200, "NotificationDeliveryFailed"), ex,
                    "Committed {EventType} notification was not delivered for ride {RideId}, offer {OfferId}, driver {DriverId}; clients will reconcile by polling",
                    outbound.GetType().Name, rideId, offerId, driverId);
            }
        }
    }

    private static (Guid? RideId, Guid? OfferId, Guid? DriverId) Correlation(OutboundEvent outbound) => outbound switch
    {
        OutboundEvent.OfferReceived received => (received.Offer.RideId, received.Offer.OfferId, received.DriverId),
        OutboundEvent.OfferClosed closed => (closed.RideId, closed.OfferId, closed.DriverId),
        OutboundEvent.StatusChanged changed => (changed.View.RideId, null, changed.DriverId),
        OutboundEvent.Location location => (location.Payload.RideId, null, location.DriverId),
        _ => (null, null, null),
    };
}
