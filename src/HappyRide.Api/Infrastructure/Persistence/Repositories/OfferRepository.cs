using HappyRide.Api.Features.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace HappyRide.Api.Infrastructure.Persistence.Repositories;

public sealed class OfferRepository(AppDbContext db) : IOfferRepository
{
    public async Task AddAsync(Offer offer, CancellationToken cancellationToken = default)
    {
        await db.Offers.AddAsync(offer, cancellationToken);
    }

    public Task<Offer?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Offers.FirstOrDefaultAsync(offer => offer.Id == id, cancellationToken);

    public Task<List<Offer>> PendingAsync(CancellationToken cancellationToken = default) =>
        db.Offers.Where(offer => offer.Status == OfferStatus.Pending).ToListAsync(cancellationToken);

    public Task<List<Offer>> PendingForDriverAsync(Guid driverId, CancellationToken cancellationToken = default) =>
        db.Offers.Where(offer => offer.DriverId == driverId && offer.Status == OfferStatus.Pending).ToListAsync(cancellationToken);

    public Task<List<Offer>> ForRideAsync(Guid rideId, CancellationToken cancellationToken = default) =>
        db.Offers.Where(offer => offer.RideId == rideId).ToListAsync(cancellationToken);

    public Task<Guid?> ReadRideIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Offers.AsNoTracking()
            .Where(offer => offer.Id == id)
            .Select(offer => (Guid?)offer.RideId)
            .FirstOrDefaultAsync(cancellationToken);
}
