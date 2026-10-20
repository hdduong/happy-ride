namespace GrabBike.Api.SharedKernel;

public sealed class GeoPoint
{
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string Address { get; set; } = "";
}

public sealed record GeoRoute(string Type, double[][] Coordinates);
