namespace GrabBike.Api.Features.Realtime;

public sealed class NoRideEventPublisher : IRideEventPublisher
{
    public Task PublishAsync(IReadOnlyList<OutboundEvent> events, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
