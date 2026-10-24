using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Drivers;

public readonly record struct RoamRouteRequest(
    Guid DriverId,
    long Ticket,
    double StartLat,
    double StartLng,
    double TargetLat,
    double TargetLng);

public readonly record struct RoamStep(
    double Lat,
    double Lng,
    double Heading,
    double MovedM,
    bool Finished);

public interface IDriverRoam
{
    void Begin(Guid driverId, double lat, double lng);
    bool TryTarget(Guid driverId, out double lat, out double lng);
    IReadOnlyList<RoamRouteRequest> PendingRoutes();
    void SetRoute(Guid driverId, long ticket, GeoRoute route);
    bool TryAdvance(Guid driverId, double stepMeters, out RoamStep step);
    IReadOnlyList<Guid> Drivers();
    void Clear(Guid driverId);
}

public sealed class DriverRoam : IDriverRoam
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Plan> _targets = new();
    private long _nextTicket;

    public void Begin(Guid driverId, double lat, double lng)
    {
        var bearing = Random.Shared.NextDouble() * 360d;
        var distance = 200d + Random.Shared.NextDouble() * 250d;
        var target = Geo.OffsetMeters(lat, lng, bearing, distance);
        lock (_gate)
        {
            var ticket = ++_nextTicket;
            _targets[driverId] = new Plan(ticket, lat, lng, target.Lat, target.Lng);
        }
    }

    public bool TryTarget(Guid driverId, out double lat, out double lng)
    {
        lock (_gate)
        {
            if (!_targets.TryGetValue(driverId, out var plan))
            {
                lat = 0;
                lng = 0;
                return false;
            }

            lat = plan.TargetLat;
            lng = plan.TargetLng;
            return true;
        }
    }

    public IReadOnlyList<RoamRouteRequest> PendingRoutes()
    {
        lock (_gate)
        {
            return _targets
                .Where(pair => pair.Value.Route is null)
                .Select(pair => new RoamRouteRequest(
                    pair.Key,
                    pair.Value.Ticket,
                    pair.Value.StartLat,
                    pair.Value.StartLng,
                    pair.Value.TargetLat,
                    pair.Value.TargetLng))
                .ToArray();
        }
    }

    public void SetRoute(Guid driverId, long ticket, GeoRoute route)
    {
        lock (_gate)
        {
            if (!_targets.TryGetValue(driverId, out var plan) || plan.Ticket != ticket || plan.Route is not null)
                return;

            plan.Route = IsUsableRoamRoute(plan, route.Coordinates)
                ? route
                : PickupRoutes.Straight(plan.StartLat, plan.StartLng, plan.TargetLat, plan.TargetLng);
        }
    }

    public bool TryAdvance(Guid driverId, double stepMeters, out RoamStep step)
    {
        lock (_gate)
        {
            if (!_targets.TryGetValue(driverId, out var plan) || plan.Route is not { Coordinates.Length: >= 2 } route)
            {
                step = default;
                return false;
            }

            var moved = Geo.AdvanceRoute(route.Coordinates, plan.Segment, plan.OffsetM, stepMeters);
            plan.Segment = moved.Segment;
            plan.OffsetM = moved.OffsetM;
            var finished = moved.Segment >= route.Coordinates.Length - 1 || moved.MovedM < 0.5;
            step = new RoamStep(moved.Lat, moved.Lng, moved.Heading, moved.MovedM, finished);
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

    private static bool IsUsableRoamRoute(Plan plan, double[][] coordinates)
    {
        if (coordinates.Length < 2 || RouteLengthMeters(coordinates) < 50)
            return false;

        var end = coordinates[^1];
        if (end.Length < 2)
            return false;

        return Geo.HaversineMeters(plan.StartLat, plan.StartLng, end[1], end[0]) >= 100;
    }

    private static double RouteLengthMeters(double[][] coordinates)
    {
        var total = 0d;
        for (var i = 0; i < coordinates.Length - 1; i++)
        {
            if (coordinates[i].Length < 2 || coordinates[i + 1].Length < 2)
                continue;
            total += Geo.HaversineMeters(coordinates[i][1], coordinates[i][0], coordinates[i + 1][1], coordinates[i + 1][0]);
        }

        return total;
    }

    private sealed class Plan(
        long ticket,
        double startLat,
        double startLng,
        double targetLat,
        double targetLng)
    {
        public long Ticket { get; } = ticket;
        public double StartLat { get; } = startLat;
        public double StartLng { get; } = startLng;
        public double TargetLat { get; } = targetLat;
        public double TargetLng { get; } = targetLng;
        public GeoRoute? Route { get; set; }
        public int Segment { get; set; }
        public double OffsetM { get; set; }
    }
}
