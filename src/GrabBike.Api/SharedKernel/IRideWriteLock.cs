namespace GrabBike.Api.SharedKernel;

public interface IRideWriteLock
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default);
}
