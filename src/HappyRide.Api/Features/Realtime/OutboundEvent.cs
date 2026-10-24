using HappyRide.Api.Features.Dispatch;
using HappyRide.Api.Features.Drivers;
using HappyRide.Api.Features.Rides;

namespace HappyRide.Api.Features.Realtime;

public abstract record OutboundEvent
{
    public sealed record OfferReceived(Guid DriverId, OfferDto Offer) : OutboundEvent;
    public sealed record OfferClosed(Guid DriverId, Guid OfferId, Guid RideId, string Reason) : OutboundEvent;
    public sealed record StatusChanged(RideView View, Guid RiderId, Guid? DriverId) : OutboundEvent;
    public sealed record Location(Guid RiderId, Guid DriverId, DriverLocationDto Payload) : OutboundEvent;
}
