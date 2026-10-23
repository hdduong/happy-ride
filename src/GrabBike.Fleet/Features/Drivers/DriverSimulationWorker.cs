using GrabBike.Api.Features.Realtime;
using GrabBike.Api.Infrastructure.Observability;

namespace GrabBike.Api.Features.Drivers;

public sealed class DriverSimulationWorker(
    IServiceScopeFactory scopes,
    IRideNotifications notifications,
    ILogger<DriverSimulationWorker> logger) : BackgroundService
{
    public const double StepMeters = 20;

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
                        var service = scope.ServiceProvider.GetRequiredService<IDriverSimulationService>();
                        events = await service.AdvanceAsync(DateTimeOffset.UtcNow, StepMeters, stoppingToken);
                    }
                    await notifications.DeliverAsync(events, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "DriverSimulation tick failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
