using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Features.Dispatch;

internal static class OfferOutcomeHttp
{
    public static IActionResult ToActionResult(this OfferOutcome result)
    {
        return result switch
        {
            OfferOutcome.Accepted accepted => new OkObjectResult(accepted.Body),
            OfferOutcome.Declined => new OkResult(),
            OfferOutcome.NotFound => new NotFoundResult(),
            OfferOutcome.Forbidden => new StatusCodeResult(StatusCodes.Status403Forbidden),
            OfferOutcome.Conflict conflict => new ConflictObjectResult(new { reason = conflict.Reason }),
            _ => throw new InvalidOperationException($"Unhandled offer outcome {result.GetType().Name}."),
        };
    }
}
