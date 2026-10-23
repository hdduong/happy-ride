using GrabBike.Api.Features.Maps;
using Microsoft.AspNetCore.SignalR;

namespace GrabBike.Api.Features.Realtime;

public sealed class SignalRMapConfigPublisher(IHubContext<RideHub> hub) : IMapConfigPublisher
{
    public Task ChangedAsync(MapConfiguration configuration, CancellationToken cancellationToken = default) =>
        hub.Clients.All.SendAsync("MapConfigChanged", configuration, cancellationToken);
}
