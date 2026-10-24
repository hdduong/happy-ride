using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Maps;

public sealed class Quote
{
    public required Guid Id { get; init; }
    public required string Provider { get; init; }
    public required GeoPoint Pickup { get; set; }
    public required GeoPoint Dropoff { get; set; }
    public required int DistanceM { get; init; }
    public required int DurationS { get; init; }
    public required int FareVnd { get; init; }
    public required GeoRoute Route { get; set; }
}
