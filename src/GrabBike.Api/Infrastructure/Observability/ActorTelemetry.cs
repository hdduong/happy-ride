using System.Diagnostics;
using GrabBike.Api.Features.Dev;

namespace GrabBike.Api.Infrastructure.Observability;

/// <summary>Safe metadata retained after inner request scopes unwind, including exception handling.</summary>
public sealed class ActorTelemetry
{
    public Dictionary<string, object?> Fields { get; } = new();

    public ActorTelemetry(User user)
    {
        Fields["ActorId"] = user.Id;
        Fields["Username"] = user.Name;
        Fields["ActorRole"] = user.Role.ToString().ToLowerInvariant();
        Fields["Source"] = "Server";
    }

    public void Set(string name, object? value)
    {
        if (value is null) return;
        Fields[name] = value;
        Activity.Current?.SetTag(name, value is Guid guid ? guid.ToString("D") : value);
    }

    public void TagActivity()
    {
        foreach (var (key, value) in Fields)
            Activity.Current?.SetTag(key, value is Guid guid ? guid.ToString("D") : value);
    }

    public static IReadOnlyDictionary<string, object?> From(HttpContext context) =>
        context.Features.Get<ActorTelemetry>()?.Fields ?? new Dictionary<string, object?>
        {
            ["ActorRole"] = "anonymous", ["Username"] = "anonymous", ["Source"] = "Server",
        };

    public static IReadOnlyDictionary<string, object?> System => new Dictionary<string, object?>
    {
        ["ActorRole"] = "system", ["Username"] = "system", ["Source"] = "Server",
    };

    public static Guid? ParseGuid(string? raw) => Guid.TryParse(raw, out var value) && value != Guid.Empty ? value : null;
}
