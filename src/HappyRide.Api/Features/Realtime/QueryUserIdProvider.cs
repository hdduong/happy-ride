using Microsoft.AspNetCore.SignalR;

namespace HappyRide.Api.Features.Realtime;

public sealed class QueryUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        var raw = connection.GetHttpContext()?.Request.Query["userId"].ToString();
        return Guid.TryParse(raw, out var id) ? id.ToString("D") : null;
    }
}
