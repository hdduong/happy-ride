using GrabBike.Api.Data;
using GrabBike.Api.Repositories;

namespace GrabBike.Api.Services;

public interface IRideService
{
    Task<ApiResult> CreateAsync(Guid riderId, Guid quoteId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<RideView?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RideView?> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ApiResult> ArrivedAsync(Guid driverId, Guid rideId, CancellationToken cancellationToken = default);
    Task<ApiResult> StartAsync(Guid driverId, Guid rideId, CancellationToken cancellationToken = default);
    Task<ApiResult> DropoffAsync(Guid driverId, Guid rideId, CancellationToken cancellationToken = default);
    Task<ApiResult> ConfirmArrivalAsync(Guid riderId, Guid rideId, CancellationToken cancellationToken = default);
    Task<ApiResult> CancelAsync(Guid riderId, Guid rideId, CancellationToken cancellationToken = default);
    Task<List<OutboundEvent>> TickAsync(DateTimeOffset now, double stepMeters, CancellationToken cancellationToken = default);
}

public sealed class RideService(
    IQuoteRepository quotes,
    IRideRepository rides,
    IOfferRepository offers,
    IDriverRepository drivers,
    IDispatchService dispatch,
    IUnitOfWork unitOfWork,
    RideWriteLock writeLock) : IRideService
{
    public async Task<ApiResult> CreateAsync(Guid riderId, Guid quoteId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var quote = await quotes.FindAsync(quoteId, cancellationToken);
        if (quote is null)
            return new ApiResult.Empty(StatusCodes.Status404NotFound);

        var active = await rides.FindActiveForRiderAsync(riderId, cancellationToken);
        if (active is not null)
            return new ApiResult.ActiveRideConflict(active.Id);

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

        var result = new ApiResult.JsonBody<CreatedRideDto>(StatusCodes.Status201Created, new CreatedRideDto(ride.Id));
        await dispatch.AdvanceAsync(ride, now, result.Events, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<RideView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ride = await rides.FindAsync(id, cancellationToken);
        return ride is null ? null : await ViewAsync(ride, cancellationToken);
    }

    public async Task<RideView?> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userRepo = drivers;
        var ride = await rides.FindCurrentForRiderAsync(userId, cancellationToken);
        if (ride is not null)
            return await ViewAsync(ride, cancellationToken);

        var driver = await userRepo.FindAsync(userId, cancellationToken);
        if (driver?.CurrentRideId is not Guid rideId)
            return null;

        var assigned = await rides.FindAsync(rideId, cancellationToken);
        if (assigned is null || !IsActive(assigned.Status))
            return null;

        return await ViewAsync(assigned, cancellationToken);
    }

    public Task<ApiResult> ArrivedAsync(Guid driverId, Guid rideId, CancellationToken cancellationToken = default) =>
        TransitionAsync(driverId, rideId, asDriver: true, RideStatus.Accepted, RideStatus.DriverArrived, cancellationToken);

    public Task<ApiResult> StartAsync(Guid driverId, Guid rideId, CancellationToken cancellationToken = default) =>
        TransitionAsync(driverId, rideId, asDriver: true, RideStatus.DriverArrived, RideStatus.InProgress, cancellationToken);

    public Task<ApiResult> DropoffAsync(Guid driverId, Guid rideId, CancellationToken cancellationToken = default) =>
        TransitionAsync(driverId, rideId, asDriver: true, RideStatus.InProgress, RideStatus.DroppedOff, cancellationToken);

    public async Task<ApiResult> ConfirmArrivalAsync(Guid riderId, Guid rideId, CancellationToken cancellationToken = default)
    {
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var ride = await rides.FindAsync(rideId, cancellationToken);
        if (ride is null)
            return new ApiResult.Empty(StatusCodes.Status404NotFound);
        if (ride.RiderId != riderId)
            return new ApiResult.Empty(StatusCodes.Status403Forbidden);
        if (ride.Status != RideStatus.DroppedOff)
            return new ApiResult.TransitionConflict(ride.Status.ToString(), RideStatus.Completed.ToString());

        ride.Status = RideStatus.Completed;
        await ReleaseDriverAsync(ride, cancellationToken);
        var result = await StatusResultAsync(ride, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<ApiResult> CancelAsync(Guid riderId, Guid rideId, CancellationToken cancellationToken = default)
    {
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var ride = await rides.FindAsync(rideId, cancellationToken);
        if (ride is null)
            return new ApiResult.Empty(StatusCodes.Status404NotFound);
        if (ride.RiderId != riderId)
            return new ApiResult.Empty(StatusCodes.Status403Forbidden);
        if (ride.Status is not (RideStatus.Searching or RideStatus.Accepted or RideStatus.DriverArrived))
            return new ApiResult.TransitionConflict(ride.Status.ToString(), RideStatus.Cancelled.ToString());

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

    public async Task<List<OutboundEvent>> TickAsync(DateTimeOffset now, double stepMeters, CancellationToken cancellationToken = default)
    {
        await using var _ = await writeLock.AcquireAsync(cancellationToken);
        var events = new List<OutboundEvent>();
        foreach (var driver in await drivers.ListAsync(cancellationToken))
        {
            if (driver.Status != DriverStatus.OnTrip || driver.CurrentRideId is not Guid rideId)
                continue;

            var ride = await rides.FindAsync(rideId, cancellationToken);
            if (ride is null)
                continue;

            if (ride.Status == RideStatus.Accepted)
            {
                var moved = Geo.MoveToward(driver.Lat, driver.Lng, ride.Pickup.Lat, ride.Pickup.Lng, stepMeters);
                ApplyMovement(driver, moved.Lat, moved.Lng, moved.Heading, moved.MovedM);
                if (moved.MovedM >= 0.5)
                    events.Add(LocationEvent(ride, driver, now));
            }
            else if (ride.Status == RideStatus.InProgress)
            {
                var moved = Geo.AdvanceRoute(ride.Route.Coordinates, ride.SegmentIndex, ride.SegmentOffsetM, stepMeters);
                ride.SegmentIndex = moved.Segment;
                ride.SegmentOffsetM = moved.OffsetM;
                ApplyMovement(driver, moved.Lat, moved.Lng, moved.Heading, moved.MovedM);
                if (moved.MovedM >= 0.5)
                    events.Add(LocationEvent(ride, driver, now));
            }
        }

        foreach (var offer in await offers.ListAsync(cancellationToken))
        {
            if (offer.Status != OfferStatus.Pending || offer.ExpiresAt > now)
                continue;

            offer.Status = OfferStatus.Closed;
            events.Add(new OutboundEvent.OfferClosed(offer.DriverId, offer.Id, "Expired"));
        }

        foreach (var ride in await rides.ListAsync(cancellationToken))
            await dispatch.AdvanceAsync(ride, now, events, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return events;
    }

    private async Task<ApiResult> TransitionAsync(
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
            return new ApiResult.Empty(StatusCodes.Status404NotFound);

        if (asDriver)
        {
            if (ride.DriverId != actorId)
                return new ApiResult.Empty(StatusCodes.Status403Forbidden);
        }
        else if (ride.RiderId != actorId)
        {
            return new ApiResult.Empty(StatusCodes.Status403Forbidden);
        }

        if (ride.Status != required)
            return new ApiResult.TransitionConflict(ride.Status.ToString(), target.ToString());

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

    private async Task<ApiResult.JsonBody<RideView>> StatusResultAsync(Ride ride, CancellationToken cancellationToken)
    {
        var view = await ViewAsync(ride, cancellationToken);
        var result = new ApiResult.JsonBody<RideView>(StatusCodes.Status200OK, view);
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

    private static void ApplyMovement(Driver driver, double lat, double lng, double heading, double movedM)
    {
        driver.Lat = lat;
        driver.Lng = lng;
        if (movedM >= 0.5)
        {
            driver.Heading = heading;
            driver.SpeedKmh = movedM * 3.6;
        }
        else
        {
            driver.SpeedKmh = 0;
        }
    }

    private static OutboundEvent LocationEvent(Ride ride, Driver driver, DateTimeOffset now) =>
        new OutboundEvent.Location(
            ride.RiderId,
            driver.UserId,
            new DriverLocationDto(ride.Id, driver.UserId, driver.Lat, driver.Lng, driver.Heading, driver.SpeedKmh, now));

    private static bool IsActive(RideStatus status) => status is
        RideStatus.Searching or
        RideStatus.Accepted or
        RideStatus.DriverArrived or
        RideStatus.InProgress or
        RideStatus.DroppedOff;
}
