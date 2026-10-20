using GrabBike.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Controllers;

[ApiController]
[Route("api/rides")]
public sealed class RidesController(IRideService rides, RideNotifier notifier) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRideRequest? body, CancellationToken cancellationToken)
    {
        var user = CurrentUser();
        if (user.Role != UserRole.Rider)
            return StatusCode(StatusCodes.Status403Forbidden);
        if (body is null || body.QuoteId == Guid.Empty)
            return NotFound();

        var result = await rides.CreateAsync(user.Id, body.QuoteId, DateTimeOffset.UtcNow, cancellationToken);
        return await result.ToActionResult(notifier);
    }

    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken cancellationToken)
    {
        var ride = await rides.GetCurrentAsync(CurrentUser().Id, cancellationToken);
        return ride is null ? NoContent() : Ok(ride);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var ride = await rides.GetAsync(id, cancellationToken);
        return ride is null ? NotFound() : Ok(ride);
    }

    [HttpPost("{id:guid}/arrived")]
    public Task<IActionResult> Arrived(Guid id, CancellationToken cancellationToken) =>
        DriverAction(id, rides.ArrivedAsync, cancellationToken);

    [HttpPost("{id:guid}/start")]
    public Task<IActionResult> Start(Guid id, CancellationToken cancellationToken) =>
        DriverAction(id, rides.StartAsync, cancellationToken);

    [HttpPost("{id:guid}/dropoff")]
    public Task<IActionResult> Dropoff(Guid id, CancellationToken cancellationToken) =>
        DriverAction(id, rides.DropoffAsync, cancellationToken);

    [HttpPost("{id:guid}/confirm-arrival")]
    public async Task<IActionResult> ConfirmArrival(Guid id, CancellationToken cancellationToken)
    {
        var user = CurrentUser();
        if (user.Role != UserRole.Rider)
            return StatusCode(StatusCodes.Status403Forbidden);

        var result = await rides.ConfirmArrivalAsync(user.Id, id, cancellationToken);
        return await result.ToActionResult(notifier);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var user = CurrentUser();
        if (user.Role != UserRole.Rider)
            return StatusCode(StatusCodes.Status403Forbidden);

        var result = await rides.CancelAsync(user.Id, id, cancellationToken);
        return await result.ToActionResult(notifier);
    }

    private async Task<IActionResult> DriverAction(
        Guid rideId,
        Func<Guid, Guid, CancellationToken, Task<ApiResult>> action,
        CancellationToken cancellationToken)
    {
        var user = CurrentUser();
        if (user.Role != UserRole.Driver)
            return StatusCode(StatusCodes.Status403Forbidden);

        var result = await action(user.Id, rideId, cancellationToken);
        return await result.ToActionResult(notifier);
    }

    private User CurrentUser() => (User)HttpContext.Items["CurrentUser"]!;
}
