using System.Net.Http.Json;

namespace GrabBike.Api.Features.Maps;

public sealed class HttpMapConfigPublisher(HttpClient http, ILogger<HttpMapConfigPublisher> logger) : IMapConfigPublisher
{
    public async Task ChangedAsync(MapConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("/internal/map-config", configuration, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The map provider changed, but the realtime service was not notified.");
        }
    }
}
