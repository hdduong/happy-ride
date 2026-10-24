using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrabBike.Api.Features.Realtime;

public sealed class HttpRideEventPublisher(HttpClient http) : IRideEventPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task PublishAsync(IReadOnlyList<OutboundEvent> events, CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
            return;
        using var response = await http.PostAsJsonAsync("/internal/events", events, Json, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
