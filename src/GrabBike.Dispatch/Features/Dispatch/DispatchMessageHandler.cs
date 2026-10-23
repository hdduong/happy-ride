using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Dispatch;

public interface IDispatchMessageHandler
{
    Task<IReadOnlyList<OutboundEvent>> HandleAsync(DispatchMessage message, CancellationToken cancellationToken = default);
}

public sealed class DispatchMessageHandler(
    IRideRepository rides,
    IDispatchService dispatch,
    IDispatchExpiryService expiry,
    IUnitOfWork unitOfWork) : IDispatchMessageHandler
{
    public async Task<IReadOnlyList<OutboundEvent>> HandleAsync(DispatchMessage message, CancellationToken cancellationToken = default)
    {
        switch (message)
        {
            case DispatchMessage.Requested requested:
            {
                var ride = await rides.FindAsync(requested.RideId, cancellationToken);
                if (ride is null)
                    return [];
                var events = new List<OutboundEvent>();
                await dispatch.AdvanceAsync(ride, DateTimeOffset.UtcNow, events, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return events;
            }
            case DispatchMessage.RoundTimeout:
                return await expiry.AdvanceAsync(DateTimeOffset.UtcNow, cancellationToken);
            default:
                return [];
        }
    }
}
