using GrabBike.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrabBike.Api.Repositories;

public interface IPlaceRepository
{
    Task<Place?> FindAsync(string id, CancellationToken cancellationToken = default);
    Task<List<Place>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed class PlaceRepository(AppDbContext db) : IPlaceRepository
{
    public Task<Place?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        db.Places.FirstOrDefaultAsync(place => place.Id == id, cancellationToken);

    public Task<List<Place>> ListAsync(CancellationToken cancellationToken = default) =>
        db.Places.ToListAsync(cancellationToken);
}
