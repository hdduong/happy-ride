namespace GrabBike.Api.Features.Drivers;

public interface IDriverRepository
{
    Task<Driver?> FindAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<List<Driver>> ListOnTripAsync(CancellationToken cancellationToken = default);
    Task<List<Driver>> ListAvailableAsync(CancellationToken cancellationToken = default);
    Task<List<Driver>> ListAsync(CancellationToken cancellationToken = default);
}
