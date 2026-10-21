using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Drivers;
using GrabBike.Api.Infrastructure.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace GrabBike.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed partial class ObservabilityTests(PostgresFixture postgres)
{
    private const string Rider = "11111111-1111-1111-1111-111111111111";
    private const string Sensitive = "PRIVATE-request-marker";

    [Theory]
    [InlineData("/api/dev/users", false, 200, "api/dev/users")]
    [InlineData("/api/rides/current", false, 401, "api/rides/current")]
    [InlineData("/api/rides/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", true, 404, "api/rides/{id:guid}")]
    [InlineData("/missing/PRIVATE-request-marker", false, 404, "(unmatched)")]
    public async Task Completion_logs_safe_route_status_and_matching_trace(string path, bool authenticated, int status, string route)
    {
        await using var factory = new ObservabilityFactory(postgres.Container.GetConnectionString());
        using var client = factory.CreateClient();
        Assert.False(factory.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics").IsEnabled(LogLevel.Critical),
            string.Join(" | ", factory.Services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.Rules
                .Select(rule => $"{rule.ProviderName}:{rule.CategoryName}:{rule.LogLevel}:{rule.Filter}")));
        factory.Logs.Entries.Clear();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}?access_token={Sensitive}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Sensitive);
        if (authenticated)
            request.Headers.Add("X-User-Id", Rider);

        using var response = await client.SendAsync(request);
        await response.Content.ReadAsStringAsync();

        Assert.Equal(status, (int)response.StatusCode);
        var completion = Assert.Single(factory.Logs.Entries, entry => entry.Event.Id == 1000);
        Assert.Equal(status, completion.Fields["StatusCode"]);
        Assert.Equal(route, completion.Fields["Route"]);
        Assert.Equal(response.Headers.GetValues(RequestTelemetry.TraceHeader).Single(), completion.Fields["TraceId"]);
        Assert.True((double)completion.Fields["ElapsedMs"]! >= 0);
        Assert.False((bool)completion.Fields["Aborted"]!);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Exception is not null);
        AssertNoRequestContent(factory.Logs.Entries);
    }

    [Fact]
    public async Task Invalid_body_keeps_existing_400_without_logging_body_or_headers()
    {
        await using var factory = new ObservabilityFactory(postgres.Container.GetConnectionString());
        using var client = factory.CreateClient();
        factory.Logs.Entries.Clear();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes")
        {
            Content = new StringContent($"{{\"privatePayload\":\"{Sensitive}\"}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-User-Id", Rider);
        request.Headers.Add("Cookie", $"private={Sensitive}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var completion = Assert.Single(factory.Logs.Entries, entry => entry.Event.Id == 1000);
        Assert.Equal(400, completion.Fields["StatusCode"]);
        Assert.Equal("api/quotes", completion.Fields["Route"]);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Exception is not null);
        AssertNoRequestContent(factory.Logs.Entries);
    }

    [Fact]
    public async Task Unexpected_failure_returns_safe_problem_with_matching_trace_and_one_exception_log()
    {
        await using var factory = new ObservabilityFactory(postgres.Container.GetConnectionString());
        using var client = factory.CreateClient();
        factory.Logs.Entries.Clear();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/observability-test/failure/{Sensitive}?token={Sensitive}")
        {
            Content = new StringContent(Sensitive),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(body);
        var traceId = response.Headers.GetValues(RequestTelemetry.TraceHeader).Single();
        Assert.Equal(traceId, json.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain("internal diagnostic sentinel", body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain(Sensitive, body);

        var exceptionLog = Assert.Single(factory.Logs.Entries, entry => entry.Exception is not null);
        Assert.Equal(1001, exceptionLog.Event.Id);
        Assert.Equal(traceId, exceptionLog.Fields["TraceId"]);
        Assert.Equal("observability-test/failure/{id}", exceptionLog.Fields["Route"]);
        var completion = Assert.Single(factory.Logs.Entries, entry => entry.Event.Id == 1000);
        Assert.Equal(500, completion.Fields["StatusCode"]);
        Assert.Equal(LogLevel.Error, completion.Level);
        Assert.Equal(traceId, completion.Scopes["TraceId"]);
        Assert.Equal("observability-test/failure/{id}", completion.Fields["Route"]);
        AssertNoRequestContent(factory.Logs.Entries);
    }

    [Theory]
    [InlineData(true, 499, 0)]
    [InlineData(false, 500, 1)]
    public async Task Cancellation_is_499_only_when_the_request_is_actually_aborted(bool aborted, int expectedStatus, int exceptionCount)
    {
        await using var factory = new ObservabilityFactory(postgres.Container.GetConnectionString());
        using var client = factory.CreateClient();
        factory.Logs.Entries.Clear();
        var pipeline = new ApplicationBuilder(factory.Services);
        pipeline.UseMiddleware<RequestCompletionMiddleware>();
        pipeline.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true });
        pipeline.Run(_ => throw new OperationCanceledException("Simulated cancellation"));
        var context = new DefaultHttpContext { RequestServices = factory.Services };
        context.Request.Method = "GET";
        context.RequestAborted = new CancellationToken(aborted);
        context.Response.Body = new MemoryStream();

        await pipeline.Build()(context);

        var completion = Assert.Single(factory.Logs.Entries, entry => entry.Event.Id == 1000);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal(expectedStatus, completion.Fields["StatusCode"]);
        Assert.Equal(aborted, completion.Fields["Aborted"]);
        Assert.Equal(exceptionCount, factory.Logs.Entries.Count(entry => entry.Exception is not null));
    }

    [Fact]
    public async Task Failure_after_response_started_is_not_logged_as_a_success()
    {
        using var logs = new CapturedLogs();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        var original = new InvalidOperationException("Streaming response failed");
        var middleware = new RequestCompletionMiddleware(ctx =>
        {
            ctx.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
            throw original;
        }, loggerFactory.CreateLogger<RequestCompletionMiddleware>());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Same(original, thrown);
        var completion = Assert.Single(logs.Entries);
        Assert.Equal(500, completion.Fields["StatusCode"]);
        Assert.Null(completion.Exception); // The outer server/exception handler owns the exception.
    }

    [Fact]
    public void Azure_export_is_absent_without_connection_and_configuration_enables_scopes_when_present()
    {
        var disabled = WebApplication.CreateBuilder();
        disabled.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "";
        disabled.AddApplicationObservability();
        Assert.DoesNotContain(disabled.Services, descriptor => descriptor.ServiceType == typeof(TracerProvider));

        var enabled = WebApplication.CreateBuilder();
        enabled.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
        enabled.AddApplicationObservability();
        Assert.Contains(enabled.Services, descriptor => descriptor.ServiceType == typeof(TracerProvider));
        // Resolve configuration only, never a provider/host/exporter: this test makes no Azure connection.
        using var services = enabled.Services.BuildServiceProvider();
        var logging = services.GetRequiredService<IOptions<OpenTelemetryLoggerOptions>>().Value;
        Assert.True(logging.IncludeScopes);
        Assert.True(logging.IncludeFormattedMessage);
        var azure = services.GetRequiredService<IOptions<AzureMonitorOptions>>().Value;
        Assert.False(azure.EnableLiveMetrics);
        Assert.Equal(1.0f, azure.SamplingRatio);
        Assert.Null(azure.TracesPerSecond);
        Assert.False(azure.EnableTraceBasedLogsSampler);
    }

    [Theory]
    [InlineData("api/rides/{id:guid}")]
    [InlineData(null)]
    public void Exported_server_activity_omits_urls_and_uses_cached_route_or_unmatched(string? route)
    {
        var exporter = new ActivityCaptureExporter();
        var sourceName = $"observability-test-{Guid.NewGuid():N}";
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource(sourceName)
            .SetSampler(new AlwaysOnSampler())
            .AddProcessor(new SafeHttpActivityProcessor())
            .AddProcessor(new SimpleActivityExportProcessor(exporter)).Build();
        using var source = new ActivitySource(sourceName);
        using (var activity = source.StartActivity($"GET /{Sensitive}", ActivityKind.Server))
        {
            Assert.NotNull(activity);
            activity.SetTag("http.request.method", "GET");
            activity.SetTag("url.full", $"https://example.test/{Sensitive}?access_token={Sensitive}");
            activity.SetTag("url.path", $"/{Sensitive}");
            activity.SetTag("url.query", $"access_token={Sensitive}");
            activity.SetTag("http.target", $"/{Sensitive}?token={Sensitive}");
            activity.SetTag("http.url", $"https://example.test/{Sensitive}");
            activity.SetCustomProperty(RequestTelemetry.ActivityRouteProperty, route);
        }

        var exported = Assert.Single(exporter.Entries);
        var expectedRoute = route ?? "(unmatched)";
        Assert.Equal($"GET {expectedRoute}", exported.Name);
        Assert.Equal(expectedRoute, exported.Tags["http.route"]);
        Assert.DoesNotContain(exported.Tags, pair => pair.Key is "url.full" or "url.path" or "url.query" or "http.target" or "http.url");
        Assert.DoesNotContain(Sensitive, JsonSerializer.Serialize(exported));
    }

    private static void AssertNoRequestContent(IEnumerable<CapturedLog> logs)
    {
        foreach (var entry in logs)
        {
            Assert.DoesNotContain(Sensitive, entry.Message);
            Assert.DoesNotContain(Sensitive, JsonSerializer.Serialize(entry.Fields));
            Assert.DoesNotContain(Sensitive, JsonSerializer.Serialize(entry.Scopes));
        }
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class ObservabilityFactory : WebApplicationFactory<Program>
    {
        private readonly string _adminConnection;
        private readonly string _databaseName;
        private readonly string _connectionString;
        public CapturedLogs Logs { get; } = new();

        public ObservabilityFactory(string adminConnection)
        {
            _adminConnection = adminConnection;
            _databaseName = "hr" + Guid.NewGuid().ToString("N");
            _connectionString = new Npgsql.NpgsqlConnectionStringBuilder(adminConnection) { Database = _databaseName }.ConnectionString;
            using var connection = new Npgsql.NpgsqlConnection(adminConnection);
            connection.Open();
            using var command = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            command.ExecuteNonQuery();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", "");
            builder.UseSetting("ConnectionStrings:GrabBike", _connectionString);
            builder.UseSetting("Dispatch:Bus", "InMemory");
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
            builder.ConfigureServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(ObservabilityTestController).Assembly);
                foreach (var descriptor in services.Where(descriptor =>
                    descriptor.ImplementationType == typeof(DriverSimulationWorker) ||
                    descriptor.ImplementationType == typeof(DispatchExpiryWorker) ||
                    descriptor.ImplementationType == typeof(ServiceBusDispatchWorker)).ToArray())
                    services.Remove(descriptor);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try
            {
                using var connection = new Npgsql.NpgsqlConnection(_adminConnection);
                connection.Open();
                using (var terminate = new Npgsql.NpgsqlCommand(
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()",
                    connection))
                {
                    terminate.Parameters.AddWithValue("name", _databaseName);
                    terminate.ExecuteNonQuery();
                }
                using var drop = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\"", connection);
                drop.ExecuteNonQuery();
            }
            catch (Npgsql.NpgsqlException)
            {
            }
        }
    }

    private sealed record CapturedLog(LogLevel Level, EventId Event, string Message, Exception? Exception,
        IReadOnlyDictionary<string, object?> Fields, IReadOnlyDictionary<string, object?> Scopes);

    private sealed class CapturedLogs : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
        public ConcurrentQueue<CapturedLog> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
        public void Dispose() { }

        private sealed class CaptureLogger(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var fields = (state as IEnumerable<KeyValuePair<string, object?>> ?? []).ToDictionary(pair => pair.Key, pair => pair.Value);
                var scopes = new Dictionary<string, object?>();
                owner._scopes.ForEachScope((scope, values) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                        foreach (var pair in pairs)
                            // Azure Monitor keeps the first (outermost) scope value.
                            values.TryAdd(pair.Key, pair.Value);
                }, scopes);
                owner.Entries.Enqueue(new CapturedLog(level, eventId, formatter(state, exception), exception, fields, scopes));
            }
        }
    }

    private sealed record CapturedActivity(string Name, IReadOnlyDictionary<string, object?> Tags);
    private sealed class ActivityCaptureExporter : BaseExporter<Activity>
    {
        public List<CapturedActivity> Entries { get; } = [];
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
                Entries.Add(new(activity.DisplayName, activity.TagObjects.ToDictionary(pair => pair.Key, pair => pair.Value)));
            return ExportResult.Success;
        }
    }
}

// This controller is registered only by ObservabilityFactory, never by the production application.
[ApiController]
[Route("observability-test")]
public sealed class ObservabilityTestController : ControllerBase
{
    [HttpPost("failure/{id}")]
    public IActionResult Failure(string id) => throw new InvalidOperationException("internal diagnostic sentinel");
}
