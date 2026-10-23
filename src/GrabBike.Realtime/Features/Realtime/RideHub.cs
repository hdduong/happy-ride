using Microsoft.AspNetCore.SignalR;
using GrabBike.Api.Features.Dev;
using GrabBike.Api.Features.Telemetry;
using GrabBike.Api.Infrastructure.Observability;

namespace GrabBike.Api.Features.Realtime;

public sealed class RideHub(
    IUserRepository users,
    ActivityContextResolver resolver,
    ILogger<RideHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await LogConnectionAsync("ConnectionConnected");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LogConnectionAsync("ConnectionDisconnected", exception is not null);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task LogConnectionAsync(string eventName, bool failed = false)
    {
        try
        {
            if (ActorTelemetry.ParseGuid(Context.UserIdentifier) is not Guid actorId
                || await users.FindAsync(actorId) is not User user)
                return;
            var actor = new ActorTelemetry(user);
            actor.Fields["AppSessionId"] = ActorTelemetry.ParseGuid(Context.GetHttpContext()?.Request.Query["appSessionId"]);
            // Kestrel already supplies ConnectionId in its outer logging scope.
            // Keep the SignalR identifier distinct so Azure preserves both.
            actor.Fields["SignalRConnectionId"] = Context.ConnectionId;
            // Resolve at each boundary: a ride may be booked after the socket connected.
            actor.Fields["RideId"] = await resolver.ActiveRideAsync(user);
            Context.GetHttpContext()?.Features.Set(actor);
            using var scope = logger.BeginScope(actor.Fields);
            logger.LogInformation(new EventId(2301, "ServerConnectionActivity"),
                "SignalR {EventName}; transport failure {TransportFailed}", eventName, failed);
        }
        catch (Exception exception)
        {
            // Diagnostic lookup failure must not terminate an otherwise working connection.
            logger.LogWarning(new EventId(2302, "ConnectionTelemetryFailed"),
                "Connection telemetry could not resolve context; error type {ErrorType}", exception.GetType().Name);
        }
    }
}
