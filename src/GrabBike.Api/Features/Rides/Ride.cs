using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Rides;

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

public sealed class Ride
{
    public required Guid Id { get; init; }
    public required Guid RiderId { get; init; }
    public Guid? DriverId { get; set; }
    public required Guid QuoteId { get; init; }
    public RideStatus Status { get; set; }
    public required GeoPoint Pickup { get; set; }
    public required GeoPoint Dropoff { get; set; }
    public required int DistanceM { get; init; }
    public required int DurationS { get; init; }
    public required int FareVnd { get; init; }
    public required GeoRoute Route { get; set; }
    public int Round { get; set; } = 1;
    public long CreatedAt { get; set; }
    public int SegmentIndex { get; set; }
    public double SegmentOffsetM { get; set; }
}
