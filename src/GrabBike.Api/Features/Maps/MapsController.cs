using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Features.Maps;

[ApiController]
[Route("api")]
public sealed class MapsController(IMapService maps, IMapProviderResolver providers, IMapConfigPublisher publisher) : ControllerBase
{
    [HttpGet("map-config")]
    public IActionResult Config() => Ok(maps.Configuration);

    [HttpGet("places/autocomplete")]
    public async Task<IActionResult> Autocomplete(
        [FromQuery] string? q,
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        CancellationToken cancellationToken) =>
        Ok(await maps.AutocompleteAsync(q, lat, lng, cancellationToken));

    [HttpGet("places/{id}")]
    public async Task<IActionResult> Place(string id, CancellationToken cancellationToken)
    {
        var place = await maps.GetPlaceAsync(id, cancellationToken);
        return place is null ? NotFound() : Ok(place);
    }

    [HttpGet("geocode/reverse")]
    public async Task<IActionResult> Reverse(
        [FromQuery] double lat,
        [FromQuery] double lng,
        CancellationToken cancellationToken) =>
        Ok(new { address = await maps.ReverseAsync(lat, lng, cancellationToken) });

    [HttpPost("quotes")]
    public async Task<IActionResult> Quote([FromBody] QuoteRequest? body, CancellationToken cancellationToken)
    {
        if (body?.Pickup is null || body.Dropoff is null)
            return BadRequest();

        var quote = await maps.CreateQuoteAsync(body.Pickup, body.Dropoff, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, quote);
    }

    [HttpPost("dev/map-provider")]
    public async Task<IActionResult> SelectProvider([FromBody] MapProviderRequest? body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body?.Provider))
            return BadRequest();
        var error = providers.TrySelect(body.Provider);
        if (error is not null)
            return Conflict(new { error });
        await publisher.ChangedAsync(maps.Configuration, cancellationToken);
        return Ok(maps.Configuration);
    }
}

public sealed class MapProviderRequest
{
    public string? Provider { get; set; }
}
