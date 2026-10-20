using GrabBike.Api.Repositories;

namespace GrabBike.Api.Middleware;

public sealed class DevIdentityMiddleware(RequestDelegate next)
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
            var users = context.RequestServices.GetRequiredService<IUserRepository>();
            if (!context.Request.Headers.TryGetValue("X-User-Id", out var raw)
                || !Guid.TryParse(raw.ToString(), out var userId)
                || await users.FindAsync(userId, context.RequestAborted) is not User user)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.Items["CurrentUser"] = user;
        }

        await next(context);
    }
}
