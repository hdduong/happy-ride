using System.Net.Http.Json;

namespace GrabBike.Api.Features.Maps;

public sealed class HttpMapConfigPublisher(HttpClient http) : IMapConfigPublisher
{
    public async Task ChangedAsync(MapConfiguration configuration, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("/internal/map-config", configuration, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
