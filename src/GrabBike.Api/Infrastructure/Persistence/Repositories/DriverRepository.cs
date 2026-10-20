using GrabBike.Api.Features.Drivers;
using Microsoft.EntityFrameworkCore;

namespace GrabBike.Api.Infrastructure.Persistence.Repositories;

public sealed class DriverRepository(AppDbContext db) : IDriverRepository
{
    public Task<Driver?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Drivers.FirstOrDefaultAsync(driver => driver.UserId == userId, cancellationToken);

    public Task<List<Driver>> ListOnTripAsync(CancellationToken cancellationToken = default) =>
        db.Drivers.Where(driver => driver.Status == DriverStatus.OnTrip && driver.CurrentRideId != null).ToListAsync(cancellationToken);

    public Task<List<Driver>> ListAvailableAsync(CancellationToken cancellationToken = default) =>
        db.Drivers.Where(driver => driver.Status == DriverStatus.Online && driver.CurrentRideId == null).ToListAsync(cancellationToken);

    public Task<List<Driver>> ListAsync(CancellationToken cancellationToken = default) =>
        db.Drivers.ToListAsync(cancellationToken);
}
