using GrabBike.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrabBike.Api.Repositories;

public interface IDriverRepository
{
    Task<Driver?> FindAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<List<Driver>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed class DriverRepository(AppDbContext db) : IDriverRepository
{
    public Task<Driver?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Drivers.FirstOrDefaultAsync(driver => driver.UserId == userId, cancellationToken);

    public Task<List<Driver>> ListAsync(CancellationToken cancellationToken = default) =>
        db.Drivers.ToListAsync(cancellationToken);
}
