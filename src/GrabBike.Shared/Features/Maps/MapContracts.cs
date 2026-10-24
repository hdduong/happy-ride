using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Maps;

public sealed record PlaceDto(string Id, string Name, string Address, double Lat, double Lng);

public sealed record QuoteDto(
    Guid QuoteId,
    string Provider,
    int DistanceM,
    int DurationS,
    GeoRoute Route,
    int FareVnd);

public sealed class QuoteRequest
{
    public GeoPoint? Pickup { get; set; }
    public GeoPoint? Dropoff { get; set; }
}
