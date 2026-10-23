using GrabBike.Api.Features.Maps;
using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Features.Realtime;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/map-config")]
public sealed class InternalMapConfigController(IMapConfigPublisher publisher, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(MapConfiguration configurationChange, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Internal:Enabled", false))
            return NotFound();
        await publisher.ChangedAsync(configurationChange, cancellationToken);
        return NoContent();
    }
}
