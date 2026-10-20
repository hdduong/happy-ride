using GrabBike.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrabBike.Api.Repositories;

public interface IUserRepository
{
    Task<User?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<User>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<List<User>> ListAsync(CancellationToken cancellationToken = default) =>
        db.Users.ToListAsync(cancellationToken);
}
