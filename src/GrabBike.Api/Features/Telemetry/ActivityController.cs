using GrabBike.Api.Features.Dev;
using GrabBike.Api.Infrastructure.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GrabBike.Api.Features.Telemetry;

[ApiController]
[Route("api/telemetry/activity")]
[EnableRateLimiting("client-activity")]
public sealed class ActivityController(ActivityContextResolver resolver, ILogger<ActivityController> logger) : ControllerBase
{
    public static readonly EventId ClientActivityEvent = new(2300, "ClientActivity");

    [HttpPost]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Record(ClientActivity activity, CancellationToken cancellationToken)
    {
        if (Request.ContentLength > 4096)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        var user = (User)HttpContext.Items["CurrentUser"]!;
        if (!activity.IsValid(user.Role) || activity.OfferId is not null && activity.RideId is null)
            return BadRequest();
        if (activity.RideId is Guid rideId && !await resolver.BelongsToAsync(user, rideId, activity.OfferId, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden);

        // Body IDs are authoritative only for this validated diagnostic event, not business state.
        var actor = HttpContext.Features.Get<ActorTelemetry>()!;
        actor.Set("AppSessionId", activity.AppSessionId);
        actor.Set("ActivityId", activity.ActivityId);
        actor.Set("RideId", activity.RideId);
        actor.Set("OfferId", activity.OfferId);
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventName"] = activity.EventName,
            ["Action"] = activity.Action, ["SignalRConnectionId"] = activity.ConnectionId,
            ["ClientOccurredAt"] = activity.ClientOccurredAt,
        });
        // Azure retains the outermost value for duplicate scope keys. A message
        // property takes precedence over the enclosing request's Server source.
        logger.LogInformation(ClientActivityEvent, "{Source} activity {EventName}", "Browser", activity.EventName);
        return NoContent();
    }
}
