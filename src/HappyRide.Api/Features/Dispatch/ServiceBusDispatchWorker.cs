using Azure.Messaging.ServiceBus;
using HappyRide.Api.Features.Realtime;
using HappyRide.Api.SharedKernel;
using HappyRide.Api.Infrastructure.Observability;

namespace HappyRide.Api.Features.Dispatch;

public sealed class ServiceBusDispatchWorker(
    ServiceBusClient client,
    IConfiguration configuration,
    IServiceScopeFactory scopes,
    IRideWriteLock writeLock,
    IRideNotifications notifications,
    ILogger<ServiceBusDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var actorScope = logger.BeginScope(ActorTelemetry.System);
        var queue = configuration["ServiceBus:Queue"] ?? "dispatch";
        var processor = client.CreateProcessor(queue, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 1,
            AutoCompleteMessages = false,
        });
        processor.ProcessMessageAsync += HandleAsync;
        processor.ProcessErrorAsync += error =>
        {
            logger.LogError(error.Exception, "Service Bus dispatch processor failed");
            return Task.CompletedTask;
        };
        await processor.StartProcessingAsync(stoppingToken);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RecoverUnpublishedAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Dispatch recovery tick failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        await processor.StopProcessingAsync(CancellationToken.None);
        await processor.DisposeAsync();
    }

    private async Task HandleAsync(ProcessMessageEventArgs args)
    {
        using var actorScope = logger.BeginScope(ActorTelemetry.System);
        var message = ServiceBusDispatchBus.FromMessage(args.Message);
        List<OutboundEvent> events;
        if (message is DispatchMessage.Requested)
        {
            await using var _ = await writeLock.AcquireAsync(args.CancellationToken);
            events = await RunAsync(message, args.CancellationToken);
            await ScheduleTimeoutAsync(message, args.CancellationToken);
        }
        else
        {
            events = await RunAsync(message, args.CancellationToken);
            foreach (var offer in events.OfType<OutboundEvent.OfferReceived>().Select(item => item.Offer).DistinctBy(offer => (offer.RideId, offer.Round)))
                await ScheduleTimeoutAsync(new DispatchMessage.Requested(offer.RideId, offer.Round), args.CancellationToken);
        }

        await notifications.DeliverAsync(events, args.CancellationToken);
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private async Task<List<OutboundEvent>> RunAsync(DispatchMessage message, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IDispatchMessageHandler>();
        return (await handler.HandleAsync(message, cancellationToken)).ToList();
    }

    private async Task RecoverUnpublishedAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var rides = scope.ServiceProvider.GetRequiredService<HappyRide.Api.Features.Rides.IRideRepository>();
        var offers = scope.ServiceProvider.GetRequiredService<IOfferRepository>();
        var bus = scope.ServiceProvider.GetRequiredService<IDispatchBus>();
        var now = DateTimeOffset.UtcNow;
        foreach (var ride in await rides.ListSearchingAsync(cancellationToken))
        {
            var pending = await offers.ForRideAsync(ride.Id, cancellationToken);
            if (pending.Any(offer => offer.Status == OfferStatus.Pending && offer.ExpiresAt > now))
                continue;
            try
            {
                await bus.PublishAsync(new DispatchMessage.Requested(ride.Id, ride.Round), cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not republish dispatch for ride {RideId}", ride.Id);
            }
        }
    }

    private async Task ScheduleTimeoutAsync(DispatchMessage message, CancellationToken cancellationToken)
    {
        if (message is not DispatchMessage.Requested requested)
            return;
        var queue = configuration["ServiceBus:Queue"] ?? "dispatch";
        var sender = client.CreateSender(queue);
        await using (sender)
        {
            var timeout = ServiceBusDispatchBus.ToMessage(
                new DispatchMessage.RoundTimeout(requested.RideId, requested.Round),
                DateTimeOffset.UtcNow.AddSeconds(ServiceBusDispatchBus.RoundSeconds));
            await sender.SendMessageAsync(timeout, cancellationToken);
        }
    }
}
