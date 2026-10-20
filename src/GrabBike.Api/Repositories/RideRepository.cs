using GrabBike.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrabBike.Api.Repositories;

public interface IRideRepository
{
    Task AddAsync(Ride ride, CancellationToken cancellationToken = default);
    Task<Ride?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Ride?> FindActiveForRiderAsync(Guid riderId, CancellationToken cancellationToken = default);
    Task<Ride?> FindCurrentForRiderAsync(Guid riderId, CancellationToken cancellationToken = default);
    Task<List<Ride>> ListAsync(CancellationToken cancellationToken = default);
}

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

        return await db.Rides
            .Where(ride => ride.RiderId == riderId &&
                (ride.Status == RideStatus.NoDriver ||
                 ride.Status == RideStatus.Cancelled ||
                 ride.Status == RideStatus.Completed))
            .OrderByDescending(ride => ride.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<List<Ride>> ListAsync(CancellationToken cancellationToken = default) =>
        db.Rides.ToListAsync(cancellationToken);
}
