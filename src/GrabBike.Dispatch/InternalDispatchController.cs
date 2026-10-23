using GrabBike.Api.Features.Realtime;
using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Features.Dispatch;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/dispatch")]
public sealed class InternalDispatchController(IDispatchMessageHandler handler, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<IReadOnlyList<OutboundEvent>>> Post(DispatchMessage message, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Internal:Enabled", false))
            return NotFound();
        return Ok(await handler.HandleAsync(message, cancellationToken));
    }
}
