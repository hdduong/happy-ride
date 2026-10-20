using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Rides;

namespace GrabBike.Api.Features.Realtime;

public abstract record OutboundEvent
{
    public sealed record OfferReceived(Guid DriverId, OfferDto Offer) : OutboundEvent;
    public sealed record OfferClosed(Guid DriverId, Guid OfferId, string Reason) : OutboundEvent;
    public sealed record StatusChanged(RideView View, Guid RiderId, Guid? DriverId) : OutboundEvent;
    public sealed record Location(Guid RiderId, Guid DriverId, DriverLocationDto Payload) : OutboundEvent;
}
