using GrabBike.Api.Features.Drivers;

namespace GrabBike.Api.Features.Dev;

public interface IUserService
{
    Task<IReadOnlyList<DevUserDto>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed class UserService(IUserRepository users, IDriverRepository drivers) : IUserService
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
}
