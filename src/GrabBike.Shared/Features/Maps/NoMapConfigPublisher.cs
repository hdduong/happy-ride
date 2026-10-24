namespace GrabBike.Api.Features.Maps;

public sealed class NoMapConfigPublisher : IMapConfigPublisher
{
    public Task ChangedAsync(MapConfiguration configuration, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
