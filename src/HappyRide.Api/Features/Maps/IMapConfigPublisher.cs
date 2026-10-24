namespace HappyRide.Api.Features.Maps;

public interface IMapConfigPublisher
{
    Task ChangedAsync(MapConfiguration configuration, CancellationToken cancellationToken = default);
}
