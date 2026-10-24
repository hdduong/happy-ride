namespace GrabBike.Api.Features.Realtime;

public interface IRideEventPublisher
{
    Task PublishAsync(IReadOnlyList<OutboundEvent> events, CancellationToken cancellationToken = default);
}
