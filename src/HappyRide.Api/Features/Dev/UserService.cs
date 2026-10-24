using HappyRide.Api.Features.Drivers;
using HappyRide.Api.SharedKernel;

namespace HappyRide.Api.Features.Dev;

public interface IUserService
{
    Task<IReadOnlyList<DevUserDto>> ListAsync(CancellationToken cancellationToken = default);
    Task ReturnHomeAsync(User user, CancellationToken cancellationToken = default);
}

public sealed class UserService(
    IUserRepository users,
    IDriverRepository drivers,
    IUnitOfWork unitOfWork,
    IRideWriteLock writeLock,
    IDriverRoam roam) : IUserService
{
    public async Task<IReadOnlyList<DevUserDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var people = await users.ListAsync(cancellationToken);
        var fleet = await drivers.ListAsync(cancellationToken);
        var plates = fleet.ToDictionary(driver => driver.UserId, driver => driver.Plate);
        var positions = fleet.ToDictionary(driver => driver.UserId, driver => (driver.Lat, driver.Lng));
        return people
            .Select(user =>
            {
                double? lat = null;
                double? lng = null;
                if (user.Role == UserRole.Driver && positions.TryGetValue(user.Id, out var position))
                {
                    lat = position.Lat;
                    lng = position.Lng;
                }
                return new DevUserDto(
                    user.Id,
                    user.Name,
                    user.Role.ToString(),
                    user.City,
                    user.Role == UserRole.Driver && plates.TryGetValue(user.Id, out var plate) ? plate : null,
                    lat,
                    lng);
            })
            .ToList();
    }

    public async Task ReturnHomeAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Role != UserRole.Driver || !DemoHomes.TryDriver(user.Id, out var lat, out var lng))
            return;

        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var driver = await drivers.FindAsync(user.Id, cancellationToken);
        if (driver is null || driver.CurrentRideId is not null || driver.Status == DriverStatus.OnTrip)
            return;

        driver.Lat = lat;
        driver.Lng = lng;
        driver.Heading = 0;
        driver.SpeedKmh = 0;
        roam.Clear(driver.UserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
