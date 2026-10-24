using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Dispatch;

public sealed record OfferDto(
    Guid OfferId,
    Guid RideId,
    GeoPoint Pickup,
    GeoPoint Dropoff,
    int DistanceM,
    int DurationS,
    DateTimeOffset ExpiresAt,
    int Round,
    int PickupDistanceM);

public sealed record OfferClosedDto(Guid OfferId, string Reason);
