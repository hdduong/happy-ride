using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace GrabBike.Api.Infrastructure.Auth;

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
        var clientId = builder.Configuration["AzureAd:ClientId"]!;
        var audience = builder.Configuration["AzureAd:Audience"];
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidAudiences = Audiences(clientId, audience);
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
                        var allowed = context.HttpContext.RequestServices
                            .GetRequiredService<IConfiguration>()["AzureAd:AllowedClientId"];
                        if (string.IsNullOrWhiteSpace(allowed))
                            return Task.CompletedTask;

                        var caller = context.Principal?.FindFirst("azp")?.Value
                            ?? context.Principal?.FindFirst("appid")?.Value;
                        if (!string.Equals(caller, allowed, StringComparison.Ordinal))
                            context.Fail("The token is for a different application.");
                        return Task.CompletedTask;
                    },
                };
            });
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
                await context.ChallengeAsync();
                return;
            }

            await next();
        });
    }

    public static bool RequiresToken(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs");

    private static string[] Audiences(string clientId, string? audience)
    {
        var values = new List<string> { clientId, $"api://{clientId}" };
        if (!string.IsNullOrWhiteSpace(audience) && !values.Contains(audience, StringComparer.Ordinal))
            values.Add(audience);
        return values.ToArray();
    }
}
