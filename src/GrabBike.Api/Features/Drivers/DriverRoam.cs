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
    bool Finished,
    Guid? RideId,
    Guid? RiderId);

public interface IDriverRoam
{
    void Begin(Guid driverId, double lat, double lng, Guid? rideId = null, Guid? riderId = null);
    bool TryTarget(Guid driverId, out double lat, out double lng);
    IReadOnlyList<RoamRouteRequest> PendingRoutes();
    void SetRoute(Guid driverId, long ticket, GeoRoute route);
    bool TryAdvance(Guid driverId, double currentLat, double currentLng, double stepMeters, out RoamStep step);
    IReadOnlyList<Guid> Drivers();
    void Clear(Guid driverId);
}

public sealed class DriverRoam : IDriverRoam
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Plan> _targets = new();
    private long _nextTicket;

    public void Begin(Guid driverId, double lat, double lng, Guid? rideId = null, Guid? riderId = null)
    {
        var bearing = Random.Shared.NextDouble() * 360d;
        var distance = 200d + Random.Shared.NextDouble() * 250d;
        var target = Geo.OffsetMeters(lat, lng, bearing, distance);
        lock (_gate)
        {
            var ticket = ++_nextTicket;
            _targets[driverId] = new Plan(ticket, lat, lng, target.Lat, target.Lng, rideId, riderId);
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
        var chosen = RouteLengthMeters(route.Coordinates) >= 50
            ? route
            : null;
        lock (_gate)
        {
            if (!_targets.TryGetValue(driverId, out var plan) || plan.Ticket != ticket || plan.Route is not null)
                return;

            plan.Route = chosen ?? PickupRoutes.Straight(plan.StartLat, plan.StartLng, plan.TargetLat, plan.TargetLng);
        }
    }

    public bool TryAdvance(Guid driverId, double currentLat, double currentLng, double stepMeters, out RoamStep step)
    {
        lock (_gate)
        {
            if (!_targets.TryGetValue(driverId, out var plan))
            {
                step = default;
                return false;
            }

            if (plan.Route is { Coordinates.Length: >= 2 } route)
            {
                var moved = Geo.AdvanceRoute(route.Coordinates, plan.Segment, plan.OffsetM, stepMeters);
                plan.Segment = moved.Segment;
                plan.OffsetM = moved.OffsetM;
                var finished = moved.Segment >= route.Coordinates.Length - 1 || moved.MovedM < 0.5;
                step = new RoamStep(moved.Lat, moved.Lng, moved.Heading, moved.MovedM, finished, plan.RideId, plan.RiderId);
                return true;
            }

            var straight = Geo.MoveToward(currentLat, currentLng, plan.TargetLat, plan.TargetLng, stepMeters);
            var done = Geo.HaversineMeters(straight.Lat, straight.Lng, plan.TargetLat, plan.TargetLng) < 15 || straight.MovedM < 0.5;
            step = new RoamStep(straight.Lat, straight.Lng, straight.Heading, straight.MovedM, done, plan.RideId, plan.RiderId);
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
        double targetLng,
        Guid? rideId,
        Guid? riderId)
    {
        public long Ticket { get; } = ticket;
        public double StartLat { get; } = startLat;
        public double StartLng { get; } = startLng;
        public double TargetLat { get; } = targetLat;
        public double TargetLng { get; } = targetLng;
        public Guid? RideId { get; } = rideId;
        public Guid? RiderId { get; } = riderId;
        public GeoRoute? Route { get; set; }
        public int Segment { get; set; }
        public double OffsetM { get; set; }
    }
}
