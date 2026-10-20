using GrabBike.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrabBike.Api.Repositories;

public interface IOfferRepository
{
    Task AddAsync(Offer offer, CancellationToken cancellationToken = default);
    Task<Offer?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Offer>> ListAsync(CancellationToken cancellationToken = default);
    Task<List<Offer>> ForRideAsync(Guid rideId, CancellationToken cancellationToken = default);
}

public sealed class OfferRepository(AppDbContext db) : IOfferRepository
{
    public async Task AddAsync(Offer offer, CancellationToken cancellationToken = default)
    {
        await db.Offers.AddAsync(offer, cancellationToken);
    }

    public Task<Offer?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Offers.FirstOrDefaultAsync(offer => offer.Id == id, cancellationToken);

    public Task<List<Offer>> ListAsync(CancellationToken cancellationToken = default) =>
        db.Offers.ToListAsync(cancellationToken);

    public Task<List<Offer>> ForRideAsync(Guid rideId, CancellationToken cancellationToken = default) =>
        db.Offers.Where(offer => offer.RideId == rideId).ToListAsync(cancellationToken);
}
