using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Features.Dispatch;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/dispatch")]
public sealed class InternalDispatchController(IDispatchBus bus, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<IReadOnlyList<OutboundEvent>>> Post(DispatchMessage message, CancellationToken cancellationToken)
    {
        if (!InternalAccess.Enabled(configuration))
            return NotFound();
        if (!InternalAccess.Authorized(Request, configuration))
            return Unauthorized();
        return Ok(await bus.PublishAsync(message, cancellationToken));
    }
}
