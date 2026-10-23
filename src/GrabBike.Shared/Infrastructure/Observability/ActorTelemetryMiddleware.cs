using GrabBike.Api.Features.Dev;
using GrabBike.Api.Features.Telemetry;

namespace GrabBike.Api.Infrastructure.Observability;

public sealed class ActorTelemetryMiddleware(RequestDelegate next, ILogger<ActorTelemetryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopes)
    {
        if (context.Items["CurrentUser"] is not User user)
        {
            await next(context);
            return;
        }
        var actor = new ActorTelemetry(user);
        context.Features.Set(actor);
        actor.Set("AppSessionId", ActorTelemetry.ParseGuid(context.Request.Headers["X-App-Session-Id"]));
        actor.Set("ActivityId", ActorTelemetry.ParseGuid(context.Request.Headers["X-Activity-Id"]));

        // Route IDs take precedence over optional diagnostic headers. An unrelated RideId is never attached.
        var routeId = ActorTelemetry.ParseGuid(context.Request.RouteValues["id"]?.ToString());
        Guid? rideId = null;
        try
        {
            // Correlation runs before the command takes its write lock. Keep these reads in a
            // separate context so EF cannot reuse a pre-lock tracked ride/offer inside the command.
            await using var lookupScope = scopes.CreateAsyncScope();
            var resolver = lookupScope.ServiceProvider.GetRequiredService<ActivityContextResolver>();
            if (context.Request.Path.StartsWithSegments("/api/offers") && routeId is Guid offerId)
            {
                rideId = await resolver.RideForOfferAsync(user, offerId, context.RequestAborted);
                if (rideId is not null) actor.Set("OfferId", offerId);
            }
            else if (!context.Request.Path.StartsWithSegments("/api/telemetry")
                && !(HttpMethods.IsPost(context.Request.Method)
                    && context.Request.Path.Value?.TrimEnd('/').Equals("/api/rides", StringComparison.OrdinalIgnoreCase) == true))
            {
                var candidate = context.Request.Path.StartsWithSegments("/api/rides") && routeId is not null
                    ? routeId : ActorTelemetry.ParseGuid(context.Request.Headers["X-Ride-Id"]);
                if (candidate is Guid id && await resolver.BelongsToAsync(user, id, null, context.RequestAborted))
                    rideId = id;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            using var diagnosticScope = logger.BeginScope(actor.Fields);
            logger.LogWarning(new EventId(2303, "CorrelationLookupFailed"),
                "Request correlation lookup failed; error type {ErrorType}", exception.GetType().Name);
        }
        actor.Set("RideId", rideId);
        actor.TagActivity();
        using var scope = logger.BeginScope(actor.Fields);
        await next(context);
    }
}
