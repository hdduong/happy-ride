namespace GrabBike.Api;

public sealed class DevIdentityMiddleware(RequestDelegate next, RideStore store)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";
        var exempt = HttpMethods.IsGet(context.Request.Method)
            && (path.Equals("/api/dev/users", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/api/map-config", StringComparison.OrdinalIgnoreCase));
        if (!exempt)
        {
            if (!context.Request.Headers.TryGetValue("X-User-Id", out var raw)
                || !Guid.TryParse(raw.ToString(), out var userId)
                || store.FindUser(userId) is not User user)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.Items["CurrentUser"] = user;
        }

        await next(context);
    }
}

public static class GrabBikeEndpoints
{
    public static void MapGrabBikeApi(this WebApplication app)
    {
        app.MapGet("/api/dev/users", (RideStore store) => Results.Ok(store.ListUsers()));
        app.MapGet("/api/map-config", () => Results.Ok(new { provider = "Fake", style = "dark" }));

        app.MapGet("/api/places/autocomplete", (string? q, double? lat, double? lng, RideStore store) =>
            Results.Ok(store.Autocomplete(q, lat, lng)));

        app.MapGet("/api/places/{id}", (string id, RideStore store) =>
        {
            var place = store.GetPlace(id);
            return place is null ? Results.NotFound() : Results.Ok(place);
        });

        app.MapGet("/api/geocode/reverse", (double lat, double lng, RideStore store) =>
            Results.Ok(new { address = store.Reverse(lat, lng) }));

        app.MapPost("/api/quotes", (QuoteRequest? body, RideStore store) =>
        {
            if (body?.Pickup is null || body.Dropoff is null)
                return Results.BadRequest();

            var quote = store.CreateQuote(body.Pickup, body.Dropoff);
            return Results.Json(quote, statusCode: StatusCodes.Status201Created);
        });

        app.MapPost("/api/rides", async (CreateRideRequest? body, HttpContext context, RideStore store, RideNotifier notifier) =>
        {
            var user = Current(context);
            if (user.Role != UserRole.Rider)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (body is null || body.QuoteId == Guid.Empty)
                return Results.NotFound();

            var result = store.CreateRide(user.Id, body.QuoteId, DateTimeOffset.UtcNow);
            return await Write(result, notifier);
        });

        app.MapGet("/api/rides/current", (HttpContext context, RideStore store) =>
        {
            var ride = store.GetCurrent(Current(context).Id);
            return ride is null ? Results.NoContent() : Results.Ok(ride);
        });

        app.MapGet("/api/rides/{id:guid}", (Guid id, RideStore store) =>
        {
            var ride = store.GetRide(id);
            return ride is null ? Results.NotFound() : Results.Ok(ride);
        });

        app.MapGet("/api/offers/pending", (HttpContext context, RideStore store) =>
        {
            var user = Current(context);
            if (user.Role != UserRole.Driver)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            return Results.Ok(store.PendingOffers(user.Id, DateTimeOffset.UtcNow));
        });

        app.MapPost("/api/offers/{id:guid}/accept", async (Guid id, HttpContext context, RideStore store, RideNotifier notifier) =>
        {
            var user = Current(context);
            if (user.Role != UserRole.Driver)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var result = store.Accept(user.Id, id, DateTimeOffset.UtcNow);
            return await Write(result, notifier);
        });

        app.MapPost("/api/offers/{id:guid}/decline", async (Guid id, HttpContext context, RideStore store, RideNotifier notifier) =>
        {
            var user = Current(context);
            if (user.Role != UserRole.Driver)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var result = store.Decline(user.Id, id, DateTimeOffset.UtcNow);
            return await Write(result, notifier);
        });

        app.MapPost("/api/rides/{id:guid}/arrived", (Guid id, HttpContext context, RideStore store, RideNotifier notifier) =>
            DriverAction(context, store, notifier, id, store.Arrived));

        app.MapPost("/api/rides/{id:guid}/start", (Guid id, HttpContext context, RideStore store, RideNotifier notifier) =>
            DriverAction(context, store, notifier, id, store.Start));

        app.MapPost("/api/rides/{id:guid}/dropoff", (Guid id, HttpContext context, RideStore store, RideNotifier notifier) =>
            DriverAction(context, store, notifier, id, store.Dropoff));

        app.MapPost("/api/rides/{id:guid}/confirm-arrival", async (Guid id, HttpContext context, RideStore store, RideNotifier notifier) =>
        {
            var user = Current(context);
            if (user.Role != UserRole.Rider)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            return await Write(store.ConfirmArrival(user.Id, id), notifier);
        });

        app.MapPost("/api/rides/{id:guid}/cancel", async (Guid id, HttpContext context, RideStore store, RideNotifier notifier) =>
        {
            var user = Current(context);
            if (user.Role != UserRole.Rider)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            return await Write(store.Cancel(user.Id, id), notifier);
        });
    }

    private static async Task<IResult> DriverAction(
        HttpContext context,
        RideStore store,
        RideNotifier notifier,
        Guid rideId,
        Func<Guid, Guid, ApiResult> action)
    {
        var user = Current(context);
        if (user.Role != UserRole.Driver)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        return await Write(action(user.Id, rideId), notifier);
    }

    private static async Task<IResult> Write(ApiResult result, RideNotifier notifier)
    {
        if (result.Events.Count > 0)
            await notifier.PublishAsync(result.Events);

        return result switch
        {
            ApiResult.JsonBody<RideView> ride => Results.Json(ride.Body, statusCode: ride.Status),
            ApiResult.JsonBody<CreatedRideDto> created => Results.Json(created.Body, statusCode: created.Status),
            ApiResult.TransitionConflict transition => Results.Json(
                new { error = "InvalidTransition", from = transition.From, to = transition.To },
                statusCode: StatusCodes.Status409Conflict),
            ApiResult.AcceptConflict accept => Results.Json(
                new { reason = accept.Reason },
                statusCode: StatusCodes.Status409Conflict),
            ApiResult.ActiveRideConflict active => Results.Json(
                new { error = "ActiveRide", rideId = active.RideId },
                statusCode: StatusCodes.Status409Conflict),
            ApiResult.Empty empty when empty.Status == StatusCodes.Status204NoContent => Results.NoContent(),
            ApiResult.Empty empty when empty.Status == StatusCodes.Status200OK => Results.Ok(),
            ApiResult.Empty empty => Results.StatusCode(empty.Status),
            _ => throw new InvalidOperationException($"Unhandled API result {result.GetType().Name}."),
        };
    }

    private static User Current(HttpContext context) => (User)context.Items["CurrentUser"]!;
}
