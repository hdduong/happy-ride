namespace HappyRide.Api.Features.Drivers;

public sealed record DriverLocationDto(
    Guid RideId,
    Guid DriverId,
    double Lat,
    double Lng,
    double Heading,
    double SpeedKmh,
    DateTimeOffset At);
