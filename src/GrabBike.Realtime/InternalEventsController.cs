using GrabBike.Api.Features.Realtime;
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
        if (!configuration.GetValue("Internal:Enabled", false))
            return NotFound();
        await publisher.PublishAsync(events, cancellationToken);
        return NoContent();
    }
}
