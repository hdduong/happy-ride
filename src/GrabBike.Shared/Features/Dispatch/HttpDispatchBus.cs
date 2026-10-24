using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrabBike.Api.Features.Realtime;

namespace GrabBike.Api.Features.Dispatch;

public sealed class HttpDispatchBus(HttpClient http) : IDispatchBus
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public string Mode => "Http";

    public async Task<IReadOnlyList<OutboundEvent>> PublishAsync(DispatchMessage message, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("/internal/dispatch", message, Json, cancellationToken);
        response.EnsureSuccessStatusCode();
        var events = await response.Content.ReadFromJsonAsync<List<OutboundEvent>>(Json, cancellationToken);
        return events ?? [];
    }
}
