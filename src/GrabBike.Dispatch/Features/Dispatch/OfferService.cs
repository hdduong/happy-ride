using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Rides;
using GrabBike.Api.Features.Realtime;
using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Dispatch;

public interface IOfferService
{
    Task<IReadOnlyList<OfferDto>> PendingAsync(Guid driverId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<OfferOutcome> AcceptAsync(Guid driverId, Guid offerId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<OfferOutcome> DeclineAsync(Guid driverId, Guid offerId, DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed class OfferService(
    IOfferRepository offers,
    IRideRepository rides,
    IDriverRepository drivers,
    IDispatchService dispatch,
    IDispatchBus dispatchBus,
    IUnitOfWork unitOfWork,
    IRideWriteLock writeLock,
    IPickupRouter pickupRouter,
    IPickupRoutes pickupRoutes,
    ILogger<OfferService> logger) : IOfferService
{
    public async Task<IReadOnlyList<OfferDto>> PendingAsync(Guid driverId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var pending = await offers.PendingForDriverAsync(driverId, cancellationToken);
        var results = new List<OfferDto>();
        foreach (var offer in pending)
        {
            if (offer.DriverId != driverId || offer.Status != OfferStatus.Pending || offer.ExpiresAt <= now)
                continue;

            var ride = await rides.FindAsync(offer.RideId, cancellationToken);
            if (ride is null || ride.Status != RideStatus.Searching)
                continue;

            var driver = await drivers.FindAsync(driverId, cancellationToken);
            results.Add(OfferMaps.ToOffer(offer, ride, driver));
        }

        return results;
    }

    public async Task<OfferOutcome> AcceptAsync(Guid driverId, Guid offerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var approach = await PreviewPickupRouteAsync(driverId, offerId, cancellationToken);
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var offer = await offers.FindAsync(offerId, cancellationToken);
        if (offer is null)
            return new OfferOutcome.NotFound();

        var driver = await drivers.FindAsync(driverId, cancellationToken);
        if (driver is null || offer.DriverId != driverId)
            return new OfferOutcome.Forbidden();

        var ride = await rides.FindAsync(offer.RideId, cancellationToken);
        if (ride is null)
            return new OfferOutcome.NotFound();

        if (ride.Status != RideStatus.Searching)
            return new OfferOutcome.Conflict("Taken");
        if (driver.CurrentRideId is not null || driver.Status == DriverStatus.OnTrip)
            return new OfferOutcome.Conflict("Busy");
        if (offer.Status != OfferStatus.Pending || offer.ExpiresAt <= now)
            return new OfferOutcome.Conflict("Closed");

        ride.Status = RideStatus.Accepted;
        ride.DriverId = driver.UserId;
        driver.Status = DriverStatus.OnTrip;
        driver.CurrentRideId = ride.Id;
        driver.SpeedKmh = 0;
        offer.Status = OfferStatus.Accepted;
        if (approach is not null)
        {
            pickupRoutes.Start(ride.Id, approach);
            ride.ApproachRoute = approach;
        }

        var view = RideMaps.ToView(ride, driver, pickupRoutes.Geometry(ride.Id));
        var result = new OfferOutcome.Accepted(view);
        result.Events.Add(new OutboundEvent.StatusChanged(view, ride.RiderId, driver.UserId));

        foreach (var other in await offers.ForRideAsync(ride.Id, cancellationToken))
        {
            if (other.Id == offer.Id || other.Status != OfferStatus.Pending)
                continue;

            other.Status = OfferStatus.Closed;
            result.Events.Add(new OutboundEvent.OfferClosed(other.DriverId, other.Id, ride.Id, "Taken"));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation(new EventId(2100, "OfferAccepted"),
            "Offer {OfferId} accepted for ride {RideId} by driver {DriverId}; ride status {Status}",
            offer.Id, ride.Id, driverId, ride.Status.ToString());
        DispatchLogging.Committed(logger, result.Events);
        return result;
    }

    private async Task<GeoRoute?> PreviewPickupRouteAsync(Guid driverId, Guid offerId, CancellationToken cancellationToken)
    {
        var position = await drivers.ReadPositionAsync(driverId, cancellationToken);
        var rideId = await offers.ReadRideIdAsync(offerId, cancellationToken);
        if (position is null || rideId is null)
            return null;
        var pickup = await rides.ReadPickupAsync(rideId.Value, cancellationToken);
        if (pickup is null)
            return null;
        return await pickupRouter.ToPickupAsync(position.Value.Lat, position.Value.Lng, pickup, cancellationToken);
    }

    public async Task<OfferOutcome> DeclineAsync(Guid driverId, Guid offerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        OfferOutcome result;
        var changed = false;
        Guid declinedOfferId;
        Guid declinedRideId;
        await using (await writeLock.AcquireAsync(cancellationToken))
        {
            var offer = await offers.FindAsync(offerId, cancellationToken);
            if (offer is null)
                return new OfferOutcome.NotFound();
            if (offer.DriverId != driverId)
                return new OfferOutcome.Forbidden();

            result = new OfferOutcome.Declined();
            changed = offer.Status == OfferStatus.Pending;
            declinedOfferId = offer.Id;
            declinedRideId = offer.RideId;
            if (changed)
            {
                offer.Status = OfferStatus.Declined;
                var ride = await rides.FindAsync(offer.RideId, cancellationToken);
                if (ride is not null)
                    await dispatch.AdvanceAsync(ride, now, result.Events, cancellationToken);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        foreach (var created in result.Events.OfType<OutboundEvent.OfferReceived>().Select(item => item.Offer).DistinctBy(item => (item.RideId, item.Round)))
        {
            try
            {
                await dispatchBus.PublishAsync(new DispatchMessage.Requested(created.RideId, created.Round), cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Round {Round} for ride {RideId} was saved but its timeout was not scheduled", created.Round, created.RideId);
            }
        }
        if (changed)
        {
            logger.LogInformation(new EventId(2101, "OfferDeclined"),
                "Offer {OfferId} declined for ride {RideId} by driver {DriverId}",
                declinedOfferId, declinedRideId, driverId);
            DispatchLogging.Committed(logger, result.Events);
        }
        return result;
    }
}
