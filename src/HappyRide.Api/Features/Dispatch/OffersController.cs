using HappyRide.Api.Features.Dev;
using HappyRide.Api.Features.Realtime;
using Microsoft.AspNetCore.Mvc;

namespace HappyRide.Api.Features.Dispatch;

[ApiController]
[Route("api/offers")]
public sealed class OffersController(IOfferService offers, IRideNotifications notifications) : ControllerBase
{
    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken cancellationToken)
    {
        var user = CurrentUser();
        if (user.Role != UserRole.Driver)
            return StatusCode(StatusCodes.Status403Forbidden);

        return Ok(await offers.PendingAsync(user.Id, DateTimeOffset.UtcNow, cancellationToken));
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id, CancellationToken cancellationToken)
    {
        var user = CurrentUser();
        if (user.Role != UserRole.Driver)
            return StatusCode(StatusCodes.Status403Forbidden);

        var result = await offers.AcceptAsync(user.Id, id, DateTimeOffset.UtcNow, cancellationToken);
        return await RespondAsync(result, cancellationToken);
    }

    [HttpPost("{id:guid}/decline")]
    public async Task<IActionResult> Decline(Guid id, CancellationToken cancellationToken)
    {
        var user = CurrentUser();
        if (user.Role != UserRole.Driver)
            return StatusCode(StatusCodes.Status403Forbidden);

        var result = await offers.DeclineAsync(user.Id, id, DateTimeOffset.UtcNow, cancellationToken);
        return await RespondAsync(result, cancellationToken);
    }

    private async Task<IActionResult> RespondAsync(OfferOutcome result, CancellationToken cancellationToken)
    {
        // Application commands have committed before returning an outcome.
        await notifications.DeliverAsync(result.Events, cancellationToken);
        return result.ToActionResult();
    }

    private User CurrentUser() => (User)HttpContext.Items["CurrentUser"]!;
}
