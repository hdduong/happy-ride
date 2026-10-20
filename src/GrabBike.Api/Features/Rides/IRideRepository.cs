namespace GrabBike.Api.Features.Rides;

public interface IRideRepository
{
    Task AddAsync(Ride ride, CancellationToken cancellationToken = default);
    Task<Ride?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Ride?> FindActiveForRiderAsync(Guid riderId, CancellationToken cancellationToken = default);
    Task<Ride?> FindCurrentForRiderAsync(Guid riderId, CancellationToken cancellationToken = default);
    Task<List<Ride>> ListSearchingAsync(CancellationToken cancellationToken = default);
}
