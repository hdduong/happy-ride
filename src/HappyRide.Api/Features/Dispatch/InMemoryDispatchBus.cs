using HappyRide.Api.Features.Realtime;

namespace HappyRide.Api.Features.Dispatch;

/// <summary>
/// Runs a dispatch message before returning. Local tests and the demo see offers
/// on the same request that created the ride.
/// </summary>
public sealed class InMemoryDispatchBus(IServiceScopeFactory scopes) : IDispatchBus
{
    public string Mode => "InMemory";

    public async Task<IReadOnlyList<OutboundEvent>> PublishAsync(DispatchMessage message, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IDispatchMessageHandler>();
        return await handler.HandleAsync(message, cancellationToken);
    }
}
