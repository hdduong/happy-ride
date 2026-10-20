namespace GrabBike.Api.Features.Dev;

public interface IUserRepository
{
    Task<User?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<User>> ListAsync(CancellationToken cancellationToken = default);
}
