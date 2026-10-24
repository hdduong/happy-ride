namespace GrabBike.Api.Features.Maps;

public interface IPlaceRepository
{
    Task<Place?> FindAsync(string id, CancellationToken cancellationToken = default);
    Task<List<Place>> ListAsync(CancellationToken cancellationToken = default);
}
