using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using GrabBike.Api.Features.Dispatch;
using GrabBike.Api.Features.Maps;
using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace GrabBike.Api.Tests;

public class RemoteClientTests
{
    [Fact]
    public void Remote_clients_use_a_short_timeout_and_send_the_internal_key()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRemoteDispatch("http://127.0.0.1:9", "secret");
        services.AddRemoteRealtime("http://127.0.0.1:9", "secret");
        services.AddRemoteMapConfig("http://127.0.0.1:9", null);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var names = provider.GetServices<IConfigureOptions<HttpClientFactoryOptions>>()
            .Select(configure => configure.GetType().GetProperty("Name")?.GetValue(configure) as string)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct()
            .ToList();

        Assert.Equal(3, names.Count);
        var withKey = 0;
        foreach (var name in names)
        {
            var client = factory.CreateClient(name!);
            Assert.Equal(TimeSpan.FromSeconds(3), client.Timeout);
            Assert.Equal("http://127.0.0.1:9/", client.BaseAddress?.AbsoluteUri);
            if (client.DefaultRequestHeaders.TryGetValues(InternalAccess.Header, out var values))
            {
                Assert.Equal("secret", Assert.Single(values));
                withKey++;
            }
        }

        Assert.Equal(2, withKey);
    }
}

[Collection(PostgresCollection.Name)]
public class SplitGatewayTests(PostgresFixture postgres)
{
    private const string Rider = "11111111-1111-1111-1111-111111111111";
    private const string Driver = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public async Task Split_edge_forwards_feature_routes_and_keeps_identity_on_the_edge()
    {
        await using var stub = new RecordingStub();
        var settings = new Dictionary<string, string>
        {
            ["Services:Rides"] = stub.Url,
            ["Services:Dispatch"] = stub.Url,
            ["Services:Fleet"] = stub.Url,
            ["Services:Realtime"] = stub.Url,
        };
        await using var gateway = new GatewayFactory(postgres.Container.GetConnectionString(), settings);
        using var client = gateway.Client(Rider);

        foreach (var path in new[]
        {
            "/api/rides",
            "/api/rides/current",
            "/api/offers",
            "/api/offers/pending",
            "/api/map-config",
            "/api/places/autocomplete?q=Ben",
            "/api/geocode/reverse?lat=10.7&lng=106.7",
            "/api/quotes",
            "/hubs/ride",
            "/hubs/ride/negotiate",
        })
        {
            using var response = path is "/api/rides" or "/api/quotes" or "/hubs/ride/negotiate"
                ? await client.PostAsync(path, Json("{}"))
                : await client.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} -> {(int)response.StatusCode} {body}");
            Assert.Equal("proxied", body);
        }

        using var provider = await client.PostAsync("/api/dev/map-provider", Json("{\"provider\":\"Fake\"}"));
        Assert.Equal("proxied", await provider.Content.ReadAsStringAsync());

        using var anonymous = gateway.CreateClient();
        using var map = await anonymous.GetAsync("/api/map-config");
        Assert.Equal("proxied", await map.Content.ReadAsStringAsync());

        using var users = await client.GetAsync("/api/dev/users");
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
        Assert.Contains(Rider, await users.Content.ReadAsStringAsync());

        using var driver = gateway.Client(Driver);
        using var home = await driver.PostAsync($"/api/dev/users/{Driver}/home", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.NoContent, home.StatusCode);

        using var telemetry = await client.PostAsJsonAsync("/api/telemetry/activity", new { });
        Assert.Equal(HttpStatusCode.BadRequest, telemetry.StatusCode);

        var beforeDenied = stub.Calls.Count;
        using var denied = await anonymous.GetAsync("/api/rides/current");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(beforeDenied, stub.Calls.Count);

        var seen = stub.Calls.Select(call => call.Path).ToArray();
        Assert.Contains(seen, path => path == "/api/rides");
        Assert.Contains(seen, path => path == "/api/rides/current");
        Assert.Contains(seen, path => path == "/api/offers/pending");
        Assert.Contains(seen, path => path.StartsWith("/api/places/autocomplete", StringComparison.Ordinal));
        Assert.Contains(seen, path => path.StartsWith("/api/geocode/reverse", StringComparison.Ordinal));
        Assert.Contains(seen, path => path == "/api/quotes");
        Assert.Contains(seen, path => path == "/api/dev/map-provider");
        Assert.Contains(seen, path => path == "/hubs/ride");
        Assert.Contains(seen, path => path.StartsWith("/hubs/ride/negotiate", StringComparison.Ordinal));
        Assert.Contains(stub.Calls, call => call.Path == "/api/rides/current" && call.User == Rider);
        Assert.DoesNotContain(seen, path => path == "/api/dev/users" || path.Contains("/home", StringComparison.Ordinal) || path.StartsWith("/api/telemetry", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_partial_service_url_keeps_rides_and_maps_in_process()
    {
        var settings = new Dictionary<string, string>
        {
            ["Services:Rides"] = "http://127.0.0.1:9",
        };
        await using var gateway = new GatewayFactory(postgres.Container.GetConnectionString(), settings);
        using var client = gateway.Client(Rider);

        using var config = await client.GetAsync("/api/map-config");
        var body = await config.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, config.StatusCode);
        Assert.Contains("Fake", body);
        Assert.DoesNotContain("proxied", body);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private sealed class GatewayFactory : WebApplicationFactory<Program>
    {
        private readonly string _adminConnection;
        private readonly string _databaseName;
        private readonly string _connectionString;
        private readonly IReadOnlyDictionary<string, string> _settings;

        public GatewayFactory(string adminConnection, IReadOnlyDictionary<string, string> settings)
        {
            _adminConnection = adminConnection;
            _settings = settings;
            _databaseName = "hr" + Guid.NewGuid().ToString("N");
            _connectionString = new Npgsql.NpgsqlConnectionStringBuilder(adminConnection) { Database = _databaseName }.ConnectionString;
            using var connection = new Npgsql.NpgsqlConnection(adminConnection);
            connection.Open();
            using var command = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            command.ExecuteNonQuery();
        }

        public HttpClient Client(string userId)
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("X-User-Id", userId);
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", "");
            builder.UseSetting("ConnectionStrings:GrabBike", _connectionString);
            foreach (var (key, value) in _settings)
                builder.UseSetting(key, value);
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

    private sealed class RecordingStub : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;
        public ConcurrentQueue<Call> Calls { get; } = new();
        public string Url { get; }

        public RecordingStub()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            Url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add($"{Url}/");
            _listener.Start();
            _loop = Task.Run(ListenAsync);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _listener.Close();
            try
            {
                await _loop;
            }
            catch (Exception)
            {
            }
        }

        private async Task ListenAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (!_listener.IsListening)
                {
                    break;
                }

                try
                {
                    await context.Request.InputStream.CopyToAsync(Stream.Null);
                    var path = context.Request.Url?.PathAndQuery ?? "/";
                    Calls.Enqueue(new Call(context.Request.HttpMethod, path, context.Request.Headers["X-User-Id"]));
                    var payload = "proxied"u8.ToArray();
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "text/plain";
                    context.Response.ContentLength64 = payload.Length;
                    await context.Response.OutputStream.WriteAsync(payload);
                    context.Response.OutputStream.Close();
                }
                catch
                {
                    try
                    {
                        context.Response.Abort();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    private sealed record Call(string Method, string Path, string? User);
}
