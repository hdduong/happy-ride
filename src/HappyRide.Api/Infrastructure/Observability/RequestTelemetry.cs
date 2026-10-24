using System.Diagnostics;

namespace HappyRide.Api.Infrastructure.Observability;

/// <summary>Safe, stable request metadata cached before exception handling clears the endpoint.</summary>
public sealed record RequestTelemetry(string TraceId, string Method, string Route)
{
    public const string TraceHeader = "X-Trace-Id";
    public const string ActivityRouteProperty = "HappyRide.RouteTemplate";
    public const string UnmatchedRoute = "(unmatched)";

    public static RequestTelemetry From(HttpContext context) =>
        context.Features.Get<RequestTelemetry>() ?? new(
            Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
            context.Request.Method,
            UnmatchedRoute);
}
