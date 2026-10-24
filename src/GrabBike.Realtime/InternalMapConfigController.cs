using GrabBike.Api.Features.Maps;
using GrabBike.Api.Infrastructure;
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
        if (!InternalAccess.Enabled(configuration))
            return NotFound();
        if (!InternalAccess.Authorized(Request, configuration))
            return Unauthorized();
        await publisher.ChangedAsync(configurationChange, cancellationToken);
        return NoContent();
    }
}
