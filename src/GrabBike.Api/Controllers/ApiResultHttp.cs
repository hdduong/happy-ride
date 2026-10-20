using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Controllers;

public static class ApiResultHttp
{
    public static async Task<IActionResult> ToActionResult(this ApiResult result, RideNotifier notifier)
    {
        if (result.Events.Count > 0)
            await notifier.PublishAsync(result.Events);

        return result switch
        {
            ApiResult.JsonBody<RideView> ride => new JsonResult(ride.Body) { StatusCode = ride.Status },
            ApiResult.JsonBody<CreatedRideDto> created => new JsonResult(created.Body) { StatusCode = created.Status },
            ApiResult.TransitionConflict transition => new JsonResult(new
            {
                error = "InvalidTransition",
                from = transition.From,
                to = transition.To,
            })
            { StatusCode = StatusCodes.Status409Conflict },
            ApiResult.AcceptConflict accept => new JsonResult(new { reason = accept.Reason })
            {
                StatusCode = StatusCodes.Status409Conflict,
            },
            ApiResult.ActiveRideConflict active => new JsonResult(new { error = "ActiveRide", rideId = active.RideId })
            {
                StatusCode = StatusCodes.Status409Conflict,
            },
            ApiResult.Empty empty when empty.Status == StatusCodes.Status204NoContent => new NoContentResult(),
            ApiResult.Empty empty when empty.Status == StatusCodes.Status200OK => new OkResult(),
            ApiResult.Empty empty => new StatusCodeResult(empty.Status),
            _ => throw new InvalidOperationException($"Unhandled API result {result.GetType().Name}."),
        };
    }
}
