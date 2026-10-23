using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Features.Realtime;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/events")]
public sealed class InternalEventsController(IRideEventPublisher publisher, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(List<OutboundEvent> events, CancellationToken cancellationToken)
    {
        if (!InternalAccess.Enabled(configuration))
            return NotFound();
        if (!InternalAccess.Authorized(Request, configuration))
            return Unauthorized();
        await publisher.PublishAsync(events, cancellationToken);
        return NoContent();
    }
}
