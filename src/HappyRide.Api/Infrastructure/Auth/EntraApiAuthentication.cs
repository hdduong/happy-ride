using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace HappyRide.Api.Infrastructure.Auth;

public static class EntraApiAuthentication
{
    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["AzureAd:TenantId"])
        && !string.IsNullOrWhiteSpace(configuration["AzureAd:ClientId"]);

    public static void AddEntraApiTokens(this WebApplicationBuilder builder)
    {
        if (!IsConfigured(builder.Configuration))
            return;

        var tenantId = builder.Configuration["AzureAd:TenantId"]!;
        var authentication = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => ConfigureBearer(options, tenantId, TokenAudiences(builder.Configuration), previousScheme: false));
        var previousTenant = builder.Configuration["AzureAd:PreviousTenantId"];
        var previousClient = builder.Configuration["AzureAd:PreviousClientId"];
        if (!string.IsNullOrWhiteSpace(previousTenant) && !string.IsNullOrWhiteSpace(previousClient))
        {
            authentication.AddJwtBearer(PreviousScheme, options => ConfigureBearer(
                options,
                previousTenant,
                TokenAudiences(previousClient, builder.Configuration["AzureAd:PreviousAudience"]),
                previousScheme: true));
        }
        builder.Services.AddAuthorization();
    }

    public static void UseEntraApiTokens(this WebApplication app)
    {
        if (!IsConfigured(app.Configuration))
            return;

        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (context, next) =>
        {
            if (RequiresToken(context.Request.Path) && context.User.Identity?.IsAuthenticated != true)
            {
                var previousTenant = context.RequestServices.GetRequiredService<IConfiguration>()["AzureAd:PreviousTenantId"];
                if (!string.IsNullOrWhiteSpace(previousTenant))
                {
                    var previous = await context.AuthenticateAsync(PreviousScheme);
                    if (previous.Succeeded && previous.Principal is not null)
                        context.User = previous.Principal;
                }
            }

            if (RequiresToken(context.Request.Path) && context.User.Identity?.IsAuthenticated != true)
            {
                await context.ChallengeAsync();
                return;
            }

            await next();
        });
    }

    public static bool RequiresToken(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs");

    public const string PreviousScheme = "EntraPrevious";

    public static string[] TokenAudiences(IConfiguration configuration)
    {
        var values = new List<string>();
        AddAudience(values, configuration["AzureAd:ClientId"], configuration["AzureAd:Audience"]);
        if (string.IsNullOrWhiteSpace(configuration["AzureAd:PreviousTenantId"]))
            AddAudience(values, configuration["AzureAd:PreviousClientId"], configuration["AzureAd:PreviousAudience"]);
        return values.ToArray();
    }

    public static string[] TokenAudiences(string? clientId, string? audience)
    {
        var values = new List<string>();
        AddAudience(values, clientId, audience);
        return values.ToArray();
    }

    private static void ConfigureBearer(JwtBearerOptions options, string tenantId, string[] audiences, bool previousScheme)
    {
        options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        options.MapInboundClaims = false;
        options.TokenValidationParameters.ValidAudiences = audiences;
        options.TokenValidationParameters.ValidIssuers =
        [
            $"https://login.microsoftonline.com/{tenantId}/v2.0",
            $"https://sts.windows.net/{tenantId}/",
        ];
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var caller = context.Principal?.FindFirst("azp")?.Value
                    ?? context.Principal?.FindFirst("appid")?.Value;
                if (!CallerAccepted(config, caller, previousScheme))
                    context.Fail("The token is for a different application.");
                return Task.CompletedTask;
            },
        };
    }

    private static bool CallerAccepted(IConfiguration configuration, string? caller, bool previousScheme)
    {
        var allowed = new List<string?>();
        if (previousScheme)
            allowed.Add(configuration["AzureAd:PreviousAllowedClientId"]);
        else
        {
            allowed.Add(configuration["AzureAd:AllowedClientId"]);
            if (string.IsNullOrWhiteSpace(configuration["AzureAd:PreviousTenantId"]))
                allowed.Add(configuration["AzureAd:PreviousAllowedClientId"]);
        }

        var ids = allowed.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        if (ids.Length == 0) return true;
        return ids.Any(id => string.Equals(caller, id, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddAudience(List<string> values, string? clientId, string? audience)
    {
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            if (!values.Contains(clientId, StringComparer.Ordinal)) values.Add(clientId);
            var application = $"api://{clientId}";
            if (!values.Contains(application, StringComparer.Ordinal)) values.Add(application);
        }
        if (!string.IsNullOrWhiteSpace(audience) && !values.Contains(audience, StringComparer.Ordinal))
            values.Add(audience);
    }
}
