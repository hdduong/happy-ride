using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GrabBike.Api.Tests;

[Collection(PostgresCollection.Name)]
public class EntraTokenTests(PostgresFixture postgres)
{
    private const string Tenant = "11111111-2222-3333-4444-555555555555";
    private const string ClientId = "happy-ride-api";
    private const string Issuer = "https://login.microsoftonline.com/11111111-2222-3333-4444-555555555555/v2.0";
    private const string SigningKey = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Configured_api_rejects_a_call_without_a_bearer_token()
    {
        await using var factory = new TokenFactory(postgres.Container.GetConnectionString());
        var response = await factory.CreateClient().GetAsync("/api/dev/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Configured_api_accepts_an_application_token()
    {
        await using var factory = new TokenFactory(postgres.Container.GetConnectionString());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(ClientId, $"api://{ClientId}"));

        var response = await client.GetAsync("/api/dev/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Configured_api_rejects_a_token_for_another_audience_or_caller()
    {
        await using var factory = new TokenFactory(postgres.Container.GetConnectionString(), allowedClientId: "web-app");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(ClientId, "api://someone-else"));
        var wrongAudience = await client.GetAsync("/api/map-config");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("other-app", $"api://{ClientId}"));
        var wrongCaller = await client.GetAsync("/api/map-config");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongCaller.StatusCode);
    }

    private static string Token(string caller, string audience)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var jwt = new JwtSecurityToken(
            issuer: Issuer,
            audience: audience,
            claims: [new Claim("azp", caller)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private sealed class TokenFactory : WebApplicationFactory<Program>
    {
        private readonly string _adminConnection;
        private readonly string _databaseName;
        private readonly string _connectionString;
        private readonly string? _allowedClientId;

        public TokenFactory(string adminConnection, string? allowedClientId = null)
        {
            _adminConnection = adminConnection;
            _allowedClientId = allowedClientId;
            _databaseName = "hr" + Guid.NewGuid().ToString("N");
            _connectionString = new Npgsql.NpgsqlConnectionStringBuilder(adminConnection) { Database = _databaseName }.ConnectionString;
            using var connection = new Npgsql.NpgsqlConnection(adminConnection);
            connection.Open();
            using var command = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            command.ExecuteNonQuery();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", "");
            builder.UseSetting("ConnectionStrings:GrabBike", _connectionString);
            builder.UseSetting("Dispatch:Bus", "InMemory");
            builder.UseSetting("AzureAd:TenantId", Tenant);
            builder.UseSetting("AzureAd:ClientId", ClientId);
            builder.UseSetting("AzureAd:Audience", $"api://{ClientId}");
            if (_allowedClientId is not null)
                builder.UseSetting("AzureAd:AllowedClientId", _allowedClientId);
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
                    var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    configuration.SigningKeys.Add(key);
                    options.Authority = null;
                    options.MetadataAddress = null!;
                    options.Configuration = configuration;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                    options.TokenValidationParameters.ValidateIssuer = true;
                    options.TokenValidationParameters.ValidIssuer = Issuer;
                    options.TokenValidationParameters.ValidIssuers = [Issuer];
                    options.TokenValidationParameters.ValidateAudience = true;
                    options.TokenValidationParameters.ValidAudiences = [ClientId, $"api://{ClientId}"];
                    options.TokenValidationParameters.IssuerSigningKey = key;
                    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                });
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
}
