namespace HappyRide.Api.Features.Dispatch;

public enum OfferStatus
{
    Pending,
    Accepted,
    Declined,
    Closed,
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
