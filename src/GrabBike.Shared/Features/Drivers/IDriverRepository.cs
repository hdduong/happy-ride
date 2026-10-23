namespace GrabBike.Api.Features.Drivers;

public interface IDriverRepository
{
    Task<Driver?> FindAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<List<Driver>> ListOnTripAsync(CancellationToken cancellationToken = default);
    Task<List<Driver>> ListAvailableAsync(CancellationToken cancellationToken = default);
    Task<List<Driver>> ListNearbyAsync(double lat, double lng, double radiusMeters, CancellationToken cancellationToken = default);
    Task<List<Driver>> ListAsync(CancellationToken cancellationToken = default);
    Task<(double Lat, double Lng)?> ReadPositionAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<List<(Guid UserId, Guid RideId)>> ReadOnTripAsync(CancellationToken cancellationToken = default);
}
