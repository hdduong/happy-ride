using System.Text.Json.Serialization;

namespace GrabBike.Api.Features.Telemetry;

// Deliberately no arbitrary message, URL, error text, username, or payload fields.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClientActivity(
    Guid ActivityId,
    Guid AppSessionId,
    string EventName,
    string? Action = null,
    Guid? RideId = null,
    Guid? OfferId = null,
    string? ConnectionId = null,
    DateTimeOffset? ClientOccurredAt = null)
{
    private static readonly HashSet<string> Events = [
        "AppOpened", "AppBackgrounded", "AppResumed", "AppClosed",
        "RideContextStarted", "RideContextEnded", "OfferDisplayed",
        "ActionRequested", "ActionSucceeded", "ActionFailed",
        "ConnectionConnected", "ConnectionReconnecting", "ConnectionReconnected", "ConnectionClosed", "ConnectionFailed",
    ];
    private static readonly HashSet<string> RiderActions = ["Book", "Cancel", "ConfirmArrival"];
    private static readonly HashSet<string> DriverActions = ["AcceptOffer", "DeclineOffer", "Arrived", "Start", "Dropoff"];

    public bool IsValid(Dev.UserRole role)
    {
        if (ActivityId == Guid.Empty || AppSessionId == Guid.Empty || !Events.Contains(EventName)
            || RideId == Guid.Empty || OfferId == Guid.Empty
            || ConnectionId is { Length: > 128 }
            || ConnectionId?.Any(char.IsControl) == true)
            return false;
        var isAction = EventName is "ActionRequested" or "ActionSucceeded" or "ActionFailed";
        if (isAction != (Action is not null))
            return false;
        if (isAction && !(role == Dev.UserRole.Rider ? RiderActions : DriverActions).Contains(Action!))
            return false;
        if ((EventName is "RideContextStarted" or "RideContextEnded" or "OfferDisplayed"
            || isAction && Action != "Book" || EventName == "ActionSucceeded" && Action == "Book") && RideId is null)
            return false;
        if (EventName == "OfferDisplayed" && (role != Dev.UserRole.Driver || OfferId is null))
            return false;
        if (Action is "AcceptOffer" or "DeclineOffer" && OfferId is null)
            return false;
        return true;
    }
}
