using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace GrabBike.Api.Infrastructure.Observability;

public sealed class RequestCompletionMiddleware(RequestDelegate next, ILogger<RequestCompletionMiddleware> logger)
{
    public static readonly EventId Completed = new(1000, "RequestCompleted");

    public async Task InvokeAsync(HttpContext context)
    {
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
            ?? RequestTelemetry.UnmatchedRoute;
        var telemetry = new RequestTelemetry(
            Activity.Current?.TraceId.ToString() ?? ActivityTraceId.CreateRandom().ToString(),
            context.Request.Method,
            route);
        context.Features.Set(telemetry);
        Activity.Current?.SetCustomProperty(RequestTelemetry.ActivityRouteProperty, route);
        context.Response.Headers[RequestTelemetry.TraceHeader] = telemetry.TraceId;
        context.Response.OnStarting(() =>
        {
            // Exception handling clears response headers before writing ProblemDetails.
            context.Response.Headers[RequestTelemetry.TraceHeader] = telemetry.TraceId;
            return Task.CompletedTask;
        });

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = telemetry.TraceId,
            ["Method"] = telemetry.Method,
            ["Route"] = telemetry.Route,
        });
        var started = Stopwatch.GetTimestamp();
        int? failedStatus = null;
        try
        {
            await next(context);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException && context.RequestAborted.IsCancellationRequested)
        {
            failedStatus = StatusCodes.Status499ClientClosedRequest;
            throw;
        }
        catch
        {
            // A streaming response may already have sent 200 before failing. Do not log success.
            // ExceptionHandler owns the exception log; completion records only the outcome.
            failedStatus = StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            var aborted = context.RequestAborted.IsCancellationRequested;
            var status = failedStatus ?? context.Response.StatusCode;
            if (aborted && status < 400)
                status = StatusCodes.Status499ClientClosedRequest;
            logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Information, Completed,
                "HTTP {Method} {Route} completed {StatusCode} in {ElapsedMs} ms; trace {TraceId}; aborted {Aborted}",
                telemetry.Method, telemetry.Route, status, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                telemetry.TraceId, aborted);
        }
    }
}
