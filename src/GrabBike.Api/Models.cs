using System.Text.Json.Serialization;

namespace GrabBike.Api;

public enum UserRole
{
    Rider,
    Driver,
}

public enum DriverStatus
{
    Online,
    OnTrip,
}

public enum RideStatus
{
    Searching,
    Accepted,
    DriverArrived,
    InProgress,
    DroppedOff,
    Completed,
    NoDriver,
    Cancelled,
}

public enum OfferStatus
{
    Pending,
    Accepted,
    Declined,
    Closed,
}

public sealed class User
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required UserRole Role { get; init; }
    public required string City { get; init; }
}

public sealed class Driver
{
    public required Guid UserId { get; init; }
    public required string Name { get; init; }
    public required string Plate { get; init; }
    public required string City { get; init; }
    public DriverStatus Status { get; set; } = DriverStatus.Online;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double Heading { get; set; }
    public double SpeedKmh { get; set; }
    public Guid? CurrentRideId { get; set; }
}

public sealed class Place
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required double Lat { get; init; }
    public required double Lng { get; init; }
}

public sealed class GeoPoint
{
    public double Lat { get; init; }
    public double Lng { get; init; }
    public string Address { get; init; } = "";
}

public sealed record GeoRoute(string Type, double[][] Coordinates);

public sealed class Quote
{
    public required Guid Id { get; init; }
    public required string Provider { get; init; }
    public required GeoPoint Pickup { get; init; }
    public required GeoPoint Dropoff { get; init; }
    public required int DistanceM { get; init; }
    public required int DurationS { get; init; }
    public required int FareVnd { get; init; }
    public required GeoRoute Route { get; init; }
}

public sealed class Ride
{
    public required Guid Id { get; init; }
    public required Guid RiderId { get; init; }
    public Guid? DriverId { get; set; }
    public required Guid QuoteId { get; init; }
    public RideStatus Status { get; set; }
    public required GeoPoint Pickup { get; init; }
    public required GeoPoint Dropoff { get; init; }
    public required int DistanceM { get; init; }
    public required int DurationS { get; init; }
    public required int FareVnd { get; init; }
    public required GeoRoute Route { get; init; }
    public int Round { get; set; } = 1;
    public int SegmentIndex { get; set; }
    public double SegmentOffsetM { get; set; }
}

public sealed class Offer
{
    public required Guid Id { get; init; }
    public required Guid RideId { get; init; }
    public required Guid DriverId { get; init; }
    public required int Round { get; init; }
    public OfferStatus Status { get; set; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed record DevUserDto(
    Guid Id,
    string Name,
    string Role,
    string City,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Plate);

public sealed record PlaceDto(string Id, string Name, string Address, double Lat, double Lng);

public sealed record QuoteDto(
    Guid QuoteId,
    string Provider,
    int DistanceM,
    int DurationS,
    GeoRoute Route,
    int FareVnd);

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
    DriverSnapshot? Driver);

public sealed record OfferDto(
    Guid OfferId,
    Guid RideId,
    GeoPoint Pickup,
    GeoPoint Dropoff,
    int DistanceM,
    int DurationS,
    DateTimeOffset ExpiresAt,
    int Round);

public sealed record DriverLocationDto(
    Guid RideId,
    Guid DriverId,
    double Lat,
    double Lng,
    double Heading,
    double SpeedKmh,
    DateTimeOffset At);

public sealed record OfferClosedDto(Guid OfferId, string Reason);

public sealed class QuoteRequest
{
    public GeoPoint? Pickup { get; set; }
    public GeoPoint? Dropoff { get; set; }
}

public sealed class CreateRideRequest
{
    public Guid QuoteId { get; set; }
}

public abstract record OutboundEvent
{
    public sealed record OfferReceived(Guid DriverId, OfferDto Offer) : OutboundEvent;
    public sealed record OfferClosed(Guid DriverId, Guid OfferId, string Reason) : OutboundEvent;
    public sealed record StatusChanged(RideView View, Guid RiderId, Guid? DriverId) : OutboundEvent;
    public sealed record Location(Guid RiderId, Guid DriverId, DriverLocationDto Payload) : OutboundEvent;
}

public abstract record ApiResult
{
    public List<OutboundEvent> Events { get; } = [];

    public sealed record JsonBody<T>(int Status, T Body) : ApiResult;
    public sealed record Empty(int Status) : ApiResult;
    public sealed record TransitionConflict(string From, string To) : ApiResult;
    public sealed record AcceptConflict(string Reason) : ApiResult;
}
