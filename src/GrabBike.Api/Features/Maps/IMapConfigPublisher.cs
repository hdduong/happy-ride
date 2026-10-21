namespace GrabBike.Api.Features.Maps;

public interface IMapConfigPublisher
{
    Task ChangedAsync(MapConfiguration configuration, CancellationToken cancellationToken = default);
}
