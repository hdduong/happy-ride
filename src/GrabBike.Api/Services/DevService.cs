using GrabBike.Api.Repositories;

namespace GrabBike.Api.Services;

public interface IDevService
{
    Task<IReadOnlyList<DevUserDto>> ListUsersAsync(CancellationToken cancellationToken = default);
}

public sealed class DevService(IUserRepository users, IDriverRepository drivers) : IDevService
{
    public async Task<IReadOnlyList<DevUserDto>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var people = await users.ListAsync(cancellationToken);
        var fleet = await drivers.ListAsync(cancellationToken);
        var plates = fleet.ToDictionary(driver => driver.UserId, driver => driver.Plate);
        return people
            .Select(user => new DevUserDto(
                user.Id,
                user.Name,
                user.Role.ToString(),
                user.City,
                user.Role == UserRole.Driver && plates.TryGetValue(user.Id, out var plate) ? plate : null))
            .ToList();
    }
}
