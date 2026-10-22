using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Rides;

public sealed record CreatedRideDto(Guid RideId);

public sealed record DriverSnapshot(Guid UserId, string Name, string Plate, double Lat, double Lng);

public sealed record RideView(
    Guid RideId,
    string Status,
    GeoPoint Pickup,
    GeoPoint Dropoff,
    int DistanceM,
    int DurationS,
    int FareVnd,
    GeoRoute Route,
    int Round,
    DriverSnapshot? Driver,
    GeoRoute? ApproachRoute = null);

public sealed class CreateRideRequest
{
    public Guid QuoteId { get; set; }
}
