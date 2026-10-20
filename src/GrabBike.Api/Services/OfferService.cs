using GrabBike.Api.Data;
using GrabBike.Api.Repositories;

namespace GrabBike.Api.Services;

public interface IOfferService
{
    Task<IReadOnlyList<OfferDto>> PendingAsync(Guid driverId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<ApiResult> AcceptAsync(Guid driverId, Guid offerId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<ApiResult> DeclineAsync(Guid driverId, Guid offerId, DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed class OfferService(
    IOfferRepository offers,
    IRideRepository rides,
    IDriverRepository drivers,
    IDispatchService dispatch,
    IUnitOfWork unitOfWork,
    RideWriteLock writeLock) : IOfferService
{
    public async Task<IReadOnlyList<OfferDto>> PendingAsync(Guid driverId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var pending = await offers.ListAsync(cancellationToken);
        var results = new List<OfferDto>();
        foreach (var offer in pending)
        {
            if (offer.DriverId != driverId || offer.Status != OfferStatus.Pending || offer.ExpiresAt <= now)
                continue;

            var ride = await rides.FindAsync(offer.RideId, cancellationToken);
            if (ride is null || ride.Status != RideStatus.Searching)
                continue;

            var driver = await drivers.FindAsync(driverId, cancellationToken);
            results.Add(RideMaps.ToOffer(offer, ride, driver));
        }

        return results;
    }

    public async Task<ApiResult> AcceptAsync(Guid driverId, Guid offerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var offer = await offers.FindAsync(offerId, cancellationToken);
        if (offer is null)
            return new ApiResult.Empty(StatusCodes.Status404NotFound);

        var driver = await drivers.FindAsync(driverId, cancellationToken);
        if (driver is null || offer.DriverId != driverId)
            return new ApiResult.Empty(StatusCodes.Status403Forbidden);

        var ride = await rides.FindAsync(offer.RideId, cancellationToken);
        if (ride is null)
            return new ApiResult.Empty(StatusCodes.Status404NotFound);

        if (ride.Status != RideStatus.Searching)
            return new ApiResult.AcceptConflict("Taken");
        if (driver.CurrentRideId is not null || driver.Status == DriverStatus.OnTrip)
            return new ApiResult.AcceptConflict("Busy");
        if (offer.Status != OfferStatus.Pending)
            return new ApiResult.AcceptConflict("Closed");

        ride.Status = RideStatus.Accepted;
        ride.DriverId = driver.UserId;
        driver.Status = DriverStatus.OnTrip;
        driver.CurrentRideId = ride.Id;
        driver.SpeedKmh = 0;
        offer.Status = OfferStatus.Accepted;

        var view = RideMaps.ToView(ride, driver);
        var result = new ApiResult.JsonBody<RideView>(StatusCodes.Status200OK, view);
        result.Events.Add(new OutboundEvent.StatusChanged(view, ride.RiderId, driver.UserId));

        foreach (var other in await offers.ForRideAsync(ride.Id, cancellationToken))
        {
            if (other.Id == offer.Id || other.Status != OfferStatus.Pending)
                continue;

            other.Status = OfferStatus.Closed;
            result.Events.Add(new OutboundEvent.OfferClosed(other.DriverId, other.Id, "Taken"));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<ApiResult> DeclineAsync(Guid driverId, Guid offerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var offer = await offers.FindAsync(offerId, cancellationToken);
        if (offer is null)
            return new ApiResult.Empty(StatusCodes.Status404NotFound);
        if (offer.DriverId != driverId)
            return new ApiResult.Empty(StatusCodes.Status403Forbidden);

        var result = new ApiResult.Empty(StatusCodes.Status200OK);
        if (offer.Status == OfferStatus.Pending)
        {
            offer.Status = OfferStatus.Declined;
            var ride = await rides.FindAsync(offer.RideId, cancellationToken);
            if (ride is not null)
                await dispatch.AdvanceAsync(ride, now, result.Events, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}
