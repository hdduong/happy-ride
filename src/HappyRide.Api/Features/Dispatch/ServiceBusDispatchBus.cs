using System.Text.Json;
using Azure.Messaging.ServiceBus;
using HappyRide.Api.Features.Realtime;

namespace HappyRide.Api.Features.Dispatch;

public sealed class ServiceBusDispatchBus(ServiceBusClient client, IConfiguration configuration) : IDispatchBus
{
    public const int RoundSeconds = 30;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Mode => "ServiceBus";

    public async Task<IReadOnlyList<OutboundEvent>> PublishAsync(DispatchMessage message, CancellationToken cancellationToken = default)
    {
        var queue = configuration["ServiceBus:Queue"] ?? "dispatch";
        var sender = client.CreateSender(queue);
        await using (sender)
        {
            await sender.SendMessageAsync(ToMessage(message), cancellationToken);
        }

        return [];
    }

    public static ServiceBusMessage ToMessage(DispatchMessage message, DateTimeOffset? scheduled = null)
    {
        var (subject, rideId, round) = message switch
        {
            DispatchMessage.Requested requested => ("DispatchRequested", requested.RideId, requested.Round),
            DispatchMessage.RoundTimeout timeout => ("OfferRoundTimeout", timeout.RideId, timeout.Round),
            _ => throw new InvalidOperationException($"Unhandled dispatch message {message.GetType().Name}."),
        };
        var busMessage = new ServiceBusMessage(JsonSerializer.Serialize(new Envelope(subject, rideId, round), Json))
        {
            Subject = subject,
            MessageId = $"{rideId:D}:{round}:{subject}",
        };
        if (scheduled is DateTimeOffset at)
            busMessage.ScheduledEnqueueTime = at.UtcDateTime;
        return busMessage;
    }

    public static DispatchMessage FromMessage(ServiceBusReceivedMessage message)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(message.Body.ToString(), Json)
            ?? throw new InvalidOperationException("Dispatch message body was empty.");
        return envelope.Subject switch
        {
            "OfferRoundTimeout" => new DispatchMessage.RoundTimeout(envelope.RideId, envelope.Round),
            _ => new DispatchMessage.Requested(envelope.RideId, envelope.Round),
        };
    }

    private sealed record Envelope(string Subject, Guid RideId, int Round);
}
