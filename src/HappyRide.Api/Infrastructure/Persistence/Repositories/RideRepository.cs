using HappyRide.Api.Features.Rides;
using HappyRide.Api.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HappyRide.Api.Infrastructure.Persistence.Repositories;

public sealed class RideRepository(AppDbContext db) : IRideRepository
{
    private static readonly RideStatus[] Active =
    [
        RideStatus.Searching,
        RideStatus.Accepted,
        RideStatus.DriverArrived,
        RideStatus.InProgress,
        RideStatus.DroppedOff,
    ];

    public async Task AddAsync(Ride ride, CancellationToken cancellationToken = default)
    {
        await db.Rides.AddAsync(ride, cancellationToken);
    }

    public Task<Ride?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Rides.FirstOrDefaultAsync(ride => ride.Id == id, cancellationToken);

    public Task<Ride?> FindActiveForRiderAsync(Guid riderId, CancellationToken cancellationToken = default) =>
        db.Rides
            .Where(ride => ride.RiderId == riderId && Active.Contains(ride.Status))
            .OrderByDescending(ride => ride.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Ride?> FindCurrentForRiderAsync(Guid riderId, CancellationToken cancellationToken = default)
    {
        var active = await FindActiveForRiderAsync(riderId, cancellationToken);
        if (active is not null)
            return active;

        // A completed trip stays on the screen that confirmed it. Opening the rider
        // again asks for the current ride, so that trip must not come back. Keep it
        // in this query so an older cancelled or no-driver ride does not replace it.
        var latest = await db.Rides
            .Where(ride => ride.RiderId == riderId &&
                (ride.Status == RideStatus.NoDriver ||
                 ride.Status == RideStatus.Cancelled ||
                 ride.Status == RideStatus.Completed))
            .OrderByDescending(ride => ride.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return latest is { Status: RideStatus.Completed } ? null : latest;
    }

    public Task<List<Ride>> ListSearchingAsync(CancellationToken cancellationToken = default) =>
        db.Rides.Where(ride => ride.Status == RideStatus.Searching).ToListAsync(cancellationToken);

    public async Task<GeoPoint?> ReadPickupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var point = await db.Rides.AsNoTracking()
            .Where(ride => ride.Id == id)
            .Select(ride => new { ride.Pickup.Lat, ride.Pickup.Lng })
            .FirstOrDefaultAsync(cancellationToken);
        return point is null ? null : new GeoPoint { Lat = point.Lat, Lng = point.Lng };
    }

    public Task<bool> IsAcceptedAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Rides.AsNoTracking().AnyAsync(ride => ride.Id == id && ride.Status == RideStatus.Accepted, cancellationToken);
}
