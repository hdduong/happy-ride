using System.Diagnostics;
using OpenTelemetry;

namespace HappyRide.Api.Infrastructure.Observability;

/// <summary>Runs before export; URLs and query strings never leave this process through HTTP spans.</summary>
public sealed class SafeHttpActivityProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        if (activity.Kind is not (ActivityKind.Server or ActivityKind.Client))
            return;

        // Remove both current and legacy HTTP semantic-convention attributes.
        activity.SetTag("url.full", null);
        activity.SetTag("url.query", null);
        activity.SetTag("url.fragment", null);
        activity.SetTag("http.url", null);
        activity.SetTag("http.target", null);
        activity.SetTag("url.path", null);
        if (activity.Kind == ActivityKind.Server)
        {
            var route = activity.GetCustomProperty(RequestTelemetry.ActivityRouteProperty) as string
                ?? activity.GetTagItem("http.route") as string
                ?? RequestTelemetry.UnmatchedRoute;
            var method = activity.GetTagItem("http.request.method") ?? activity.GetTagItem("http.method") ?? "HTTP";
            activity.SetTag("http.route", route);
            activity.DisplayName = $"{method} {route}";
            // Error messages may contain provider URLs; the exception log owns diagnostics.
            if (activity.Status == ActivityStatusCode.Error)
                activity.SetStatus(ActivityStatusCode.Error);
        }
    }
}
