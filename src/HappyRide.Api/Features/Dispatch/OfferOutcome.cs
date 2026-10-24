using HappyRide.Api.Features.Realtime;
using HappyRide.Api.Features.Rides;

namespace HappyRide.Api.Features.Dispatch;

public abstract record OfferOutcome
{
    public List<OutboundEvent> Events { get; } = [];
    public sealed record Accepted(RideView Body) : OfferOutcome;
    public sealed record Declined : OfferOutcome;
    public sealed record NotFound : OfferOutcome;
    public sealed record Forbidden : OfferOutcome;
    public sealed record Conflict(string Reason) : OfferOutcome;
}
