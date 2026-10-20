using GrabBike.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Controllers;

[ApiController]
[Route("api/offers")]
public sealed class OffersController(IOfferService offers, RideNotifier notifier) : ControllerBase
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
        return await result.ToActionResult(notifier);
    }

    [HttpPost("{id:guid}/decline")]
    public async Task<IActionResult> Decline(Guid id, CancellationToken cancellationToken)
    {
        var user = CurrentUser();
        if (user.Role != UserRole.Driver)
            return StatusCode(StatusCodes.Status403Forbidden);

        var result = await offers.DeclineAsync(user.Id, id, DateTimeOffset.UtcNow, cancellationToken);
        return await result.ToActionResult(notifier);
    }

    private User CurrentUser() => (User)HttpContext.Items["CurrentUser"]!;
}
