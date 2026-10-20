using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Features.Dev;

[ApiController]
[Route("api/dev")]
public sealed class DevController(IDevService dev) : ControllerBase
{
    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken) =>
        Ok(await dev.ListUsersAsync(cancellationToken));
}
