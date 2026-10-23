using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Drivers;

public interface IDriverRoam
{
    void Begin(Guid driverId, double lat, double lng);
    bool TryTarget(Guid driverId, out double lat, out double lng);
    IReadOnlyList<Guid> Drivers();
    void Clear(Guid driverId);
}

public sealed class DriverRoam : IDriverRoam
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (double Lat, double Lng)> _targets = new();

    public void Begin(Guid driverId, double lat, double lng)
    {
        var bearing = Random.Shared.NextDouble() * 360d;
        var distance = 200d + Random.Shared.NextDouble() * 250d;
        var target = Geo.OffsetMeters(lat, lng, bearing, distance);
        lock (_gate)
            _targets[driverId] = target;
    }

    public bool TryTarget(Guid driverId, out double lat, out double lng)
    {
        lock (_gate)
        {
            if (!_targets.TryGetValue(driverId, out var target))
            {
                lat = 0;
                lng = 0;
                return false;
            }

            lat = target.Lat;
            lng = target.Lng;
            return true;
        }
    }

    public IReadOnlyList<Guid> Drivers()
    {
        lock (_gate)
            return _targets.Keys.ToArray();
    }

    public void Clear(Guid driverId)
    {
        lock (_gate)
            _targets.Remove(driverId);
    }
}
