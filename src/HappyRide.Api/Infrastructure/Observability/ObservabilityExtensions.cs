using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Diagnostics;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HappyRide.Api.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    public const string ServiceName = "happy-ride-api";

    public static void AddApplicationObservability(this WebApplicationBuilder builder)
    {
        // Select the two intended outputs explicitly. Windows' default EventLog filter otherwise
        // overrides category filters and causes Hosting to create a raw RequestPath scope.
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            options.UseUtcTimestamp = true;
        });
        // ASP.NET hosting's ambient scope contains raw RequestPath, even when its Info logs are off.
        // Our completion middleware replaces that scope with a route template and trace ID.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

        var connectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        builder.Services.Configure<OpenTelemetryLoggerOptions>(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
        });
        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            options.RecordException = false; // IExceptionHandler emits one correlated exception log.
        });

        // Register the sanitizer before the Azure exporter. The distro already instruments HTTP.
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor<SafeHttpActivityProcessor>())
            .UseAzureMonitor(options =>
            {
                options.ConnectionString = connectionString;
                options.EnableLiveMetrics = false; // Avoid a separate unsanitized live-metrics export path.
                options.SamplingRatio = 1.0f;
                options.TracesPerSecond = null;
                options.EnableTraceBasedLogsSampler = false;
            })
            // Apply the application role after Azure's resource detectors (including App Service).
            .ConfigureResource(resource => resource.AddService(ServiceName));
    }

    public static void UseApplicationObservability(this WebApplication app)
    {
        // Routing must run first so completion and exception logs share the matched route template.
        app.UseRouting();
        app.UseMiddleware<RequestCompletionMiddleware>();
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            SuppressDiagnosticsCallback = _ => true, // The handler owns the single exception-bearing log.
        });
    }
}
