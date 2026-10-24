using HappyRide.Api.Features.Realtime;
using HappyRide.Api.Features.Rides;
using HappyRide.Api.SharedKernel;

namespace HappyRide.Api.Features.Dispatch;

public interface IDispatchExpiryService
{
    Task<List<OutboundEvent>> AdvanceAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed class DispatchExpiryService(
    IOfferRepository offers,
    IRideRepository rides,
    IDispatchService dispatch,
    IUnitOfWork unitOfWork,
    IRideWriteLock writeLock,
    ILogger<DispatchExpiryService> logger) : IDispatchExpiryService
{
    public async Task<List<OutboundEvent>> AdvanceAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var events = new List<OutboundEvent>();
        foreach (var offer in await offers.PendingAsync(cancellationToken))
        {
            if (offer.Status != OfferStatus.Pending || offer.ExpiresAt > now)
                continue;

            offer.Status = OfferStatus.Closed;
            events.Add(new OutboundEvent.OfferClosed(offer.DriverId, offer.Id, offer.RideId, "Expired"));
        }

        foreach (var ride in await rides.ListSearchingAsync(cancellationToken))
            await dispatch.AdvanceAsync(ride, now, events, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        DispatchLogging.Committed(logger, events);
        return events;
    }
}
