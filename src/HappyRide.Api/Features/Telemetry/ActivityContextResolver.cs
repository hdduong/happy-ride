using HappyRide.Api.Features.Dev;
using HappyRide.Api.Features.Dispatch;
using HappyRide.Api.Features.Drivers;
using HappyRide.Api.Features.Rides;

namespace HappyRide.Api.Features.Telemetry;

/// <summary>Resolves correlation from persisted ownership, never a browser-supplied username.</summary>
public sealed class ActivityContextResolver(IRideRepository rides, IOfferRepository offers, IDriverRepository drivers)
{
    public async Task<bool> BelongsToAsync(User user, Guid rideId, Guid? offerId, CancellationToken cancellationToken = default)
    {
        var ride = await rides.FindAsync(rideId, cancellationToken);
        if (ride is null)
            return false;
        if (offerId is Guid id)
        {
            var offer = await offers.FindAsync(id, cancellationToken);
            return user.Role == UserRole.Driver && offer?.DriverId == user.Id && offer.RideId == rideId;
        }
        return user.Role == UserRole.Rider ? ride.RiderId == user.Id : ride.DriverId == user.Id;
    }

    public async Task<Guid?> RideForOfferAsync(User user, Guid offerId, CancellationToken cancellationToken = default)
    {
        var offer = await offers.FindAsync(offerId, cancellationToken);
        return user.Role == UserRole.Driver && offer?.DriverId == user.Id ? offer.RideId : null;
    }

    public async Task<Guid?> ActiveRideAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Role == UserRole.Rider)
            return (await rides.FindActiveForRiderAsync(user.Id, cancellationToken))?.Id;
        var driver = await drivers.FindAsync(user.Id, cancellationToken);
        if (driver?.CurrentRideId is not Guid rideId)
            return null;
        var ride = await rides.FindAsync(rideId, cancellationToken);
        // Dropoff ends the driver's detailed ride context, while the rider can still confirm arrival.
        return ride?.DriverId == user.Id && ride.Status is RideStatus.Accepted or RideStatus.DriverArrived or RideStatus.InProgress
            ? ride.Id : null;
    }
}
