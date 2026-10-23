namespace GrabBike.Api.Features.Maps;

public sealed class Place
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required double Lat { get; init; }
    public required double Lng { get; init; }
}
