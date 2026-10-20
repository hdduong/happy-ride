namespace GrabBike.Api.Features.Dispatch;

public interface IOfferRepository
{
    Task AddAsync(Offer offer, CancellationToken cancellationToken = default);
    Task<Offer?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Offer>> PendingAsync(CancellationToken cancellationToken = default);
    Task<List<Offer>> PendingForDriverAsync(Guid driverId, CancellationToken cancellationToken = default);
    Task<List<Offer>> ForRideAsync(Guid rideId, CancellationToken cancellationToken = default);
}
