using HappyRide.Api.Features.Dispatch;
using Microsoft.AspNetCore.SignalR;

namespace HappyRide.Api.Features.Realtime;

public sealed class SignalRRideEventPublisher(IHubContext<RideHub> hub) : IRideEventPublisher
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
