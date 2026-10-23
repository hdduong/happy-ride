namespace GrabBike.Api.Features.Maps;

public sealed class MapOptions
{
    public string Provider { get; set; } = "Fake";
    public VietMapKeyOptions VietMap { get; set; } = new();
    public GoogleKeyOptions Google { get; set; } = new();
}

public sealed class VietMapKeyOptions
{
    public string ApiKey { get; set; } = "";
    public string TileKey { get; set; } = "";
}

public sealed class GoogleKeyOptions
{
    public string ApiKey { get; set; } = "";
    public string BrowserKey { get; set; } = "";
}

public sealed class MapProviderOverride
{
    private string? _provider;
    public string? Current => _provider;

    public void Set(string? provider) => _provider = string.IsNullOrWhiteSpace(provider) ? null : provider;
}
