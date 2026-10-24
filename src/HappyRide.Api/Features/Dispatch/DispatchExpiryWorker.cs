using HappyRide.Api.Features.Realtime;
using HappyRide.Api.Infrastructure.Observability;

namespace HappyRide.Api.Features.Dispatch;

public sealed class DispatchExpiryWorker(
    IServiceScopeFactory scopes,
    IRideNotifications notifications,
    ILogger<DispatchExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var actorScope = logger.BeginScope(ActorTelemetry.System);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    List<OutboundEvent> events;
                    await using (var scope = scopes.CreateAsyncScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IDispatchExpiryService>();
                        events = await service.AdvanceAsync(DateTimeOffset.UtcNow, stoppingToken);
                    }
                    await notifications.DeliverAsync(events, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "DispatchExpiry tick failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
