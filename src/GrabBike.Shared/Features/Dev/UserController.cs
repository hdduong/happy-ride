using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Features.Dev;

[ApiController]
[Route("api/dev")]
public sealed class UserController(IUserService users) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await users.ListAsync(cancellationToken));

    [HttpPost("users/{id:guid}/home")]
    public async Task<IActionResult> ReturnHome(Guid id, CancellationToken cancellationToken)
    {
        var user = (User)HttpContext.Items["CurrentUser"]!;
        if (user.Id != id)
            return Forbid();
        await users.ReturnHomeAsync(user, cancellationToken);
        return NoContent();
    }
}
