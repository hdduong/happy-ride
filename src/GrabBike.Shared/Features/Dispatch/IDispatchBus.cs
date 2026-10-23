using System.Text.Json.Serialization;
using GrabBike.Api.Features.Realtime;

namespace GrabBike.Api.Features.Dispatch;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DispatchMessage.Requested), "requested")]
[JsonDerivedType(typeof(DispatchMessage.RoundTimeout), "timeout")]
public abstract record DispatchMessage
{
    public sealed record Requested(Guid RideId, int Round) : DispatchMessage;
    public sealed record RoundTimeout(Guid RideId, int Round) : DispatchMessage;
}

public interface IDispatchBus
{
    string Mode { get; }
    Task<IReadOnlyList<OutboundEvent>> PublishAsync(DispatchMessage message, CancellationToken cancellationToken = default);
}
