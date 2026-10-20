using GrabBike.Api.Features.Drivers;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GrabBike.Api.Infrastructure.Persistence.Repositories;

public sealed class DriverRepository(AppDbContext db) : IDriverRepository
{
    public Task<Driver?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Drivers.FirstOrDefaultAsync(driver => driver.UserId == userId, cancellationToken);

    public Task<List<Driver>> ListOnTripAsync(CancellationToken cancellationToken = default) =>
        db.Drivers.Where(driver => driver.Status == DriverStatus.OnTrip && driver.CurrentRideId != null).ToListAsync(cancellationToken);

    public Task<List<Driver>> ListAvailableAsync(CancellationToken cancellationToken = default) =>
        db.Drivers.Where(driver => driver.Status == DriverStatus.Online && driver.CurrentRideId == null).ToListAsync(cancellationToken);

    public Task<List<Driver>> ListNearbyAsync(double lat, double lng, double radiusMeters, CancellationToken cancellationToken = default)
    {
        var origin = new Point(lng, lat) { SRID = 4326 };
        return db.Drivers
            .Where(driver => driver.Status == DriverStatus.Online && driver.CurrentRideId == null)
            .Where(driver => driver.Position.IsWithinDistance(origin, radiusMeters))
            .OrderBy(driver => driver.Position.Distance(origin))
            .ToListAsync(cancellationToken);
    }

    public Task<List<Driver>> ListAsync(CancellationToken cancellationToken = default) =>
        db.Drivers.ToListAsync(cancellationToken);
}
