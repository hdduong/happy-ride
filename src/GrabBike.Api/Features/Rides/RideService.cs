using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Features.Maps;
using GrabBike.Api.Features.Realtime;
using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Rides;

public interface IRideService
{
    Task<RideOutcome> CreateAsync(Guid riderId, Guid quoteId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<RideView?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RideView?> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<RideOutcome> ArrivedAsync(Guid driverId, Guid rideId, CancellationToken cancellationToken = default);
    Task<RideOutcome> StartAsync(Guid driverId, Guid rideId, CancellationToken cancellationToken = default);
    Task<RideOutcome> DropoffAsync(Guid driverId, Guid rideId, CancellationToken cancellationToken = default);
    Task<RideOutcome> ConfirmArrivalAsync(Guid riderId, Guid rideId, CancellationToken cancellationToken = default);
    Task<RideOutcome> CancelAsync(Guid riderId, Guid rideId, CancellationToken cancellationToken = default);
}

public sealed class RideService(
    IQuoteRepository quotes,
    IRideRepository rides,
    IOfferRepository offers,
    IDriverRepository drivers,
    IDispatchBus dispatchBus,
    IUnitOfWork unitOfWork,
    IRideWriteLock writeLock,
    ILogger<RideService> logger) : IRideService
{
    public async Task<RideOutcome> CreateAsync(Guid riderId, Guid quoteId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var quote = await quotes.FindAsync(quoteId, cancellationToken);
        if (quote is null)
            return new RideOutcome.NotFound();

        var active = await rides.FindActiveForRiderAsync(riderId, cancellationToken);
        if (active is not null)
            return new RideOutcome.ActiveRideConflict(active.Id);

        var ride = new Ride
        {
            Id = Guid.NewGuid(),
            RiderId = riderId,
            QuoteId = quote.Id,
            Status = RideStatus.Searching,
            Pickup = quote.Pickup,
            Dropoff = quote.Dropoff,
            DistanceM = quote.DistanceM,
            DurationS = quote.DurationS,
            FareVnd = quote.FareVnd,
            Route = quote.Route,
            Round = 1,
            CreatedAt = now.UtcTicks,
        };
        await rides.AddAsync(ride, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var result = new RideOutcome.Created(new CreatedRideDto(ride.Id));
        try
        {
            var dispatched = await dispatchBus.PublishAsync(new DispatchMessage.Requested(ride.Id, ride.Round), cancellationToken);
            result.Events.AddRange(dispatched);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ride {RideId} was saved but dispatch was not published", ride.Id);
        }
        return result;
    }

    public async Task<RideView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ride = await rides.FindAsync(id, cancellationToken);
        return ride is null ? null : await ViewAsync(ride, cancellationToken);
    }

    public async Task<RideView?> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var ride = await rides.FindCurrentForRiderAsync(userId, cancellationToken);
        if (ride is not null)
            return await ViewAsync(ride, cancellationToken);

        var driver = await drivers.FindAsync(userId, cancellationToken);
        if (driver?.CurrentRideId is not Guid rideId)
            return null;

        var assigned = await rides.FindAsync(rideId, cancellationToken);
        if (assigned is null || !IsActive(assigned.Status))
            return null;

        return await ViewAsync(assigned, cancellationToken);
    }

    public Task<RideOutcome> ArrivedAsync(Guid driverId, Guid rideId, CancellationToken cancellationToken = default) =>
        TransitionAsync(driverId, rideId, asDriver: true, RideStatus.Accepted, RideStatus.DriverArrived, cancellationToken);

    public Task<RideOutcome> StartAsync(Guid driverId, Guid rideId, CancellationToken cancellationToken = default) =>
        TransitionAsync(driverId, rideId, asDriver: true, RideStatus.DriverArrived, RideStatus.InProgress, cancellationToken);

    public Task<RideOutcome> DropoffAsync(Guid driverId, Guid rideId, CancellationToken cancellationToken = default) =>
        TransitionAsync(driverId, rideId, asDriver: true, RideStatus.InProgress, RideStatus.DroppedOff, cancellationToken);

    public async Task<RideOutcome> ConfirmArrivalAsync(Guid riderId, Guid rideId, CancellationToken cancellationToken = default)
    {
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var ride = await rides.FindAsync(rideId, cancellationToken);
        if (ride is null)
            return new RideOutcome.NotFound();
        if (ride.RiderId != riderId)
            return new RideOutcome.Forbidden();
        if (ride.Status != RideStatus.DroppedOff)
            return new RideOutcome.TransitionConflict(ride.Status.ToString(), RideStatus.Completed.ToString());

        ride.Status = RideStatus.Completed;
        await ReleaseDriverAsync(ride, cancellationToken);
        var result = await StatusResultAsync(ride, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<RideOutcome> CancelAsync(Guid riderId, Guid rideId, CancellationToken cancellationToken = default)
    {
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var ride = await rides.FindAsync(rideId, cancellationToken);
        if (ride is null)
            return new RideOutcome.NotFound();
        if (ride.RiderId != riderId)
            return new RideOutcome.Forbidden();
        if (ride.Status is not (RideStatus.Searching or RideStatus.Accepted or RideStatus.DriverArrived))
            return new RideOutcome.TransitionConflict(ride.Status.ToString(), RideStatus.Cancelled.ToString());

        ride.Status = RideStatus.Cancelled;
        await ReleaseDriverAsync(ride, cancellationToken);
        var result = await StatusResultAsync(ride, cancellationToken);
        foreach (var offer in await offers.ForRideAsync(ride.Id, cancellationToken))
        {
            if (offer.Status != OfferStatus.Pending)
                continue;
            offer.Status = OfferStatus.Closed;
            result.Events.Add(new OutboundEvent.OfferClosed(offer.DriverId, offer.Id, "Cancelled"));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<RideOutcome> TransitionAsync(
        Guid actorId,
        Guid rideId,
        bool asDriver,
        RideStatus required,
        RideStatus target,
        CancellationToken cancellationToken)
    {
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var ride = await rides.FindAsync(rideId, cancellationToken);
        if (ride is null)
            return new RideOutcome.NotFound();

        if (asDriver)
        {
            if (ride.DriverId != actorId)
                return new RideOutcome.Forbidden();
        }
        else if (ride.RiderId != actorId)
        {
            return new RideOutcome.Forbidden();
        }

        if (ride.Status != required)
            return new RideOutcome.TransitionConflict(ride.Status.ToString(), target.ToString());

        ride.Status = target;
        if (target == RideStatus.DriverArrived && ride.DriverId is Guid arrivedDriverId)
        {
            var arrivedDriver = await drivers.FindAsync(arrivedDriverId, cancellationToken);
            if (arrivedDriver is not null)
                arrivedDriver.SpeedKmh = 0;
        }

        if (target == RideStatus.InProgress)
            await SnapToRouteStartAsync(ride, cancellationToken);

        var result = await StatusResultAsync(ride, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task SnapToRouteStartAsync(Ride ride, CancellationToken cancellationToken)
    {
        ride.SegmentIndex = 0;
        ride.SegmentOffsetM = 0;
        if (ride.Route.Coordinates.Length == 0 || ride.DriverId is not Guid driverId)
            return;

        var driver = await drivers.FindAsync(driverId, cancellationToken);
        if (driver is null)
            return;

        var start = ride.Route.Coordinates[0];
        driver.Lng = start[0];
        driver.Lat = start[1];
        driver.SpeedKmh = 0;
    }

    private async Task ReleaseDriverAsync(Ride ride, CancellationToken cancellationToken)
    {
        if (ride.DriverId is not Guid driverId)
            return;

        var driver = await drivers.FindAsync(driverId, cancellationToken);
        if (driver is null || driver.CurrentRideId != ride.Id)
            return;

        driver.CurrentRideId = null;
        driver.Status = DriverStatus.Online;
        driver.SpeedKmh = 0;
    }

    private async Task<RideOutcome.Updated> StatusResultAsync(Ride ride, CancellationToken cancellationToken)
    {
        var view = await ViewAsync(ride, cancellationToken);
        var result = new RideOutcome.Updated(view);
        result.Events.Add(new OutboundEvent.StatusChanged(view, ride.RiderId, ride.DriverId));
        return result;
    }

    private async Task<RideView> ViewAsync(Ride ride, CancellationToken cancellationToken)
    {
        Driver? driver = null;
        if (ride.DriverId is Guid driverId)
            driver = await drivers.FindAsync(driverId, cancellationToken);
        return RideMaps.ToView(ride, driver);
    }

    private static bool IsActive(RideStatus status) => status is
        RideStatus.Searching or
        RideStatus.Accepted or
        RideStatus.DriverArrived or
        RideStatus.InProgress or
        RideStatus.DroppedOff;
}
