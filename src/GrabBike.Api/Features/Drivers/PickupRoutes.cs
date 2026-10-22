using System.Collections.Concurrent;
using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Features.Drivers;

public interface IPickupRoutes
{
    void Start(Guid rideId, GeoRoute route);
    GeoRoute? Geometry(Guid rideId);
    bool TryAdvance(Guid rideId, double stepMeters, out PickupStep step);
    void Forget(Guid rideId);
}

public readonly record struct PickupStep(double Lat, double Lng, double Heading, double MovedM);

public sealed class PickupRoutes : IPickupRoutes
{
    private readonly ConcurrentDictionary<Guid, Progress> routes = new();

    public void Start(Guid rideId, GeoRoute route) =>
        routes[rideId] = new Progress(route);

    public GeoRoute? Geometry(Guid rideId) =>
        routes.TryGetValue(rideId, out var progress) ? progress.Route : null;

    public bool TryAdvance(Guid rideId, double stepMeters, out PickupStep step)
    {
        if (!routes.TryGetValue(rideId, out var progress))
        {
            step = default;
            return false;
        }

        var moved = Geo.AdvanceRoute(progress.Route.Coordinates, progress.Segment, progress.OffsetM, stepMeters);
        progress.Segment = moved.Segment;
        progress.OffsetM = moved.OffsetM;
        step = new PickupStep(moved.Lat, moved.Lng, moved.Heading, moved.MovedM);
        return true;
    }

    public void Forget(Guid rideId) => routes.TryRemove(rideId, out _);

    public static GeoRoute Straight(double fromLat, double fromLng, double toLat, double toLng) =>
        new("LineString", [[fromLng, fromLat], [toLng, toLat]]);

    private sealed class Progress(GeoRoute route)
    {
        public GeoRoute Route { get; } = route;
        public int Segment { get; set; }
        public double OffsetM { get; set; }
    }
}
