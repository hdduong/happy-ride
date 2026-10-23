using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GrabBike.Api.Infrastructure.Observability;

public sealed class UnhandledExceptionHandler(ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public static readonly EventId Unhandled = new(1001, "UnhandledRequestException");

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var telemetry = RequestTelemetry.From(context);
        var status = exception is BadHttpRequestException { StatusCode: >= 400 and < 500 } badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;
        if (status >= 500)
        {
            using var actorScope = logger.BeginScope(ActorTelemetry.From(context));
            logger.LogError(Unhandled, exception,
                "Unhandled failure for {Method} {Route}; trace {TraceId}",
                telemetry.Method, telemetry.Route, telemetry.TraceId);
        }

        context.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = status >= 500 ? "An unexpected error occurred." : "The request could not be processed.",
            Extensions = { ["traceId"] = telemetry.TraceId },
        };
        // Write JSON directly: clients receive the same safe body even for an unsupported Accept header.
        await context.Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
