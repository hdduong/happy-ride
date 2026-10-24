using HappyRide.Api.Features.Maps;
using Microsoft.EntityFrameworkCore;

namespace HappyRide.Api.Infrastructure.Persistence.Repositories;

public sealed class PlaceRepository(AppDbContext db) : IPlaceRepository
{
    public Task<Place?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        db.Places.FirstOrDefaultAsync(place => place.Id == id, cancellationToken);

    public Task<List<Place>> ListAsync(CancellationToken cancellationToken = default) =>
        db.Places.ToListAsync(cancellationToken);
}
