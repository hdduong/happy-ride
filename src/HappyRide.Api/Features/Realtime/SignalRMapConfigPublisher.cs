using HappyRide.Api.Features.Maps;
using Microsoft.AspNetCore.SignalR;

namespace HappyRide.Api.Features.Realtime;

public sealed class SignalRMapConfigPublisher(IHubContext<RideHub> hub) : IMapConfigPublisher
{
    public Task ChangedAsync(MapConfiguration configuration, CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendAsync("MapConfigChanged", configuration, cancellationToken);
}
