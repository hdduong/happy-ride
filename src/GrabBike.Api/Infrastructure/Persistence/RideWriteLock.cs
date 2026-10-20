using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Infrastructure.Persistence;

/// <summary>
/// Serializes ride writes in this process. The rows live in the database;
/// the lock matches the single API instance so two accepts cannot both read Searching.
/// </summary>
public sealed class RideWriteLock : IRideWriteLock
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Releaser(_gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
