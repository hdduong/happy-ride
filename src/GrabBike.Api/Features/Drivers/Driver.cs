using NetTopologySuite.Geometries;

namespace GrabBike.Api.Features.Drivers;

public enum DriverStatus
{
    Online,
    OnTrip,
}

public sealed class Driver
{
    public required Guid UserId { get; init; }
    public required string Name { get; set; }
    public required string Plate { get; init; }
    public required string City { get; init; }
    public DriverStatus Status { get; set; } = DriverStatus.Online;
    public double Lat { get; set; }
    public double Lng { get; set; }
    public Point Position { get; set; } = new(0, 0) { SRID = 4326 };
    public double Heading { get; set; }
    public double SpeedKmh { get; set; }
    public Guid? CurrentRideId { get; set; }
}
