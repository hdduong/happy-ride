using HappyRide.Api.Features.Drivers;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace HappyRide.Api.Infrastructure.Persistence.Repositories;

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

    public async Task<(double Lat, double Lng)?> ReadPositionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var position = await db.Drivers.AsNoTracking()
            .Where(driver => driver.UserId == userId)
            .Select(driver => new { driver.Lat, driver.Lng })
            .FirstOrDefaultAsync(cancellationToken);
        return position is null ? null : (position.Lat, position.Lng);
    }

    public async Task<List<(Guid UserId, Guid RideId)>> ReadOnTripAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Drivers.AsNoTracking()
            .Where(driver => driver.Status == DriverStatus.OnTrip && driver.CurrentRideId != null)
            .Select(driver => new { driver.UserId, RideId = driver.CurrentRideId!.Value })
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.UserId, row.RideId)).ToList();
    }
}
