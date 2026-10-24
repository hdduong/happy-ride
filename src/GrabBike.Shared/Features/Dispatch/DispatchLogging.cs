using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Features.Rides;

namespace GrabBike.Api.Features.Dispatch;

public static class DispatchLogging
{
    // Dispatch decisions can be part of booking, declining or the expiry job.
    // Their caller owns persistence and invokes this only after a successful save.
    public static void Committed(ILogger logger, IReadOnlyList<OutboundEvent> events)
    {
        foreach (var outbound in events)
        {
            switch (outbound)
            {
                case OutboundEvent.OfferReceived received:
                    logger.LogInformation(new EventId(2102, "DispatchOfferCreated"),
                        "Dispatch offer {OfferId} created for ride {RideId}, driver {DriverId}, round {Round}",
                        received.Offer.OfferId, received.Offer.RideId, received.DriverId, received.Offer.Round);
                    break;
                case OutboundEvent.OfferClosed closed:
                    logger.LogInformation(new EventId(2103, "DispatchOfferClosed"),
                        "Dispatch offer {OfferId} closed for ride {RideId}, driver {DriverId}, reason {Reason}",
                        closed.OfferId, closed.RideId, closed.DriverId, closed.Reason);
                    break;
                case OutboundEvent.StatusChanged changed when changed.View.Status == nameof(RideStatus.Searching):
                    logger.LogInformation(new EventId(2104, "DispatchRoundStarted"),
                        "Dispatch round {Round} started for ride {RideId}", changed.View.Round, changed.View.RideId);
                    break;
                case OutboundEvent.StatusChanged changed when changed.View.Status == nameof(RideStatus.NoDriver):
                    logger.LogInformation(new EventId(2105, "DispatchExhausted"),
                        "Dispatch exhausted for ride {RideId} after round {Round}; ride status {Status}",
                        changed.View.RideId, changed.View.Round, changed.View.Status);
                    break;
            }
        }
    }
}
