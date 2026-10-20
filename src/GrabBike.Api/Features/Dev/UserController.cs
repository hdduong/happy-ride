using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Features.Dev;

[ApiController]
[Route("api/dev")]
public sealed class UserController(IUserService users) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await users.ListAsync(cancellationToken));
}
