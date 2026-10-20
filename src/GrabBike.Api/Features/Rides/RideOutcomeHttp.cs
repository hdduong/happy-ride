using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Features.Rides;

internal static class RideOutcomeHttp
{
    public static IActionResult ToActionResult(this RideOutcome result)
    {
        return result switch
        {
            RideOutcome.Created created => new JsonResult(created.Body) { StatusCode = StatusCodes.Status201Created },
            RideOutcome.Updated updated => new OkObjectResult(updated.Body),
            RideOutcome.NotFound => new NotFoundResult(),
            RideOutcome.Forbidden => new StatusCodeResult(StatusCodes.Status403Forbidden),
            RideOutcome.TransitionConflict transition => new ConflictObjectResult(new
            {
                error = "InvalidTransition", from = transition.From, to = transition.To,
            }),
            RideOutcome.ActiveRideConflict active => new ConflictObjectResult(new { error = "ActiveRide", rideId = active.RideId }),
            _ => throw new InvalidOperationException($"Unhandled ride outcome {result.GetType().Name}."),
        };
    }
}
