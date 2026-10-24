namespace GrabBike.Api.Infrastructure;

public static class InternalAccess
{
    public const string Header = "X-Internal-Key";

    public static bool Enabled(IConfiguration configuration) =>
        configuration.GetValue("Internal:Enabled", false);

    public static bool Authorized(HttpRequest request, IConfiguration configuration)
    {
        var expected = configuration["Internal:Key"];
        return !string.IsNullOrWhiteSpace(expected)
            && request.Headers.TryGetValue(Header, out var sent)
            && string.Equals(sent.ToString(), expected, StringComparison.Ordinal);
    }
}
