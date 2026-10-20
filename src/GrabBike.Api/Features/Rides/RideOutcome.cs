using GrabBike.Api.Features.Realtime;

namespace GrabBike.Api.Features.Rides;

/// <summary>Application outcomes. HTTP status codes belong to the controller boundary.</summary>
public abstract record RideOutcome
{
    public List<OutboundEvent> Events { get; } = [];
    public sealed record Created(CreatedRideDto Body) : RideOutcome;
    public sealed record Updated(RideView Body) : RideOutcome;
    public sealed record NotFound : RideOutcome;
    public sealed record Forbidden : RideOutcome;
    public sealed record TransitionConflict(string From, string To) : RideOutcome;
    public sealed record ActiveRideConflict(Guid RideId) : RideOutcome;
}
