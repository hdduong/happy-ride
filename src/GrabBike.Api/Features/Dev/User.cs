namespace GrabBike.Api.Features.Dev;

public enum UserRole
{
    Rider,
    Driver,
}

public sealed class User
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required UserRole Role { get; init; }
    public required string City { get; init; }
}
