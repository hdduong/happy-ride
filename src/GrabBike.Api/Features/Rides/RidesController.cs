using GrabBike.Api.Features.Dev;
using GrabBike.Api.Features.Realtime;
using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Features.Rides;

[ApiController]
[Route("api/rides")]
public sealed class RidesController(IRideService rides, IRideNotifications notifications) : ControllerBase
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
        return await RespondAsync(result, cancellationToken);
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
        return await RespondAsync(result, cancellationToken);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var user = CurrentUser();
        if (user.Role != UserRole.Rider)
            return StatusCode(StatusCodes.Status403Forbidden);

        var result = await rides.CancelAsync(user.Id, id, cancellationToken);
        return await RespondAsync(result, cancellationToken);
    }

    private async Task<IActionResult> DriverAction(
        Guid rideId,
        Func<Guid, Guid, CancellationToken, Task<RideOutcome>> action,
        CancellationToken cancellationToken)
    {
        var user = CurrentUser();
        if (user.Role != UserRole.Driver)
            return StatusCode(StatusCodes.Status403Forbidden);

        var result = await action(user.Id, rideId, cancellationToken);
        return await RespondAsync(result, cancellationToken);
    }

    private async Task<IActionResult> RespondAsync(RideOutcome result, CancellationToken cancellationToken)
    {
        // Application commands have committed before returning an outcome.
        await notifications.DeliverAsync(result.Events, cancellationToken);
        return result.ToActionResult();
    }

    private User CurrentUser() => (User)HttpContext.Items["CurrentUser"]!;
}
