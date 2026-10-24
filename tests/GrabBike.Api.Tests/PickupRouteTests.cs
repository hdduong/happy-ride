using GrabBike.Api.Features.Drivers;
using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Tests;

public class PickupRouteTests
{
    [Fact]
    public void Offset_lands_about_the_requested_distance_away()
    {
        var target = Geo.OffsetMeters(21.03342, 105.82845, 90, 300);
        var distance = Geo.HaversineMeters(21.03342, 105.82845, target.Lat, target.Lng);
        Assert.InRange(distance, 290, 310);
    }

    [Fact]
    public void Advance_follows_the_stored_road_instead_of_the_straight_chord()
    {
        var routes = new PickupRoutes();
        var rideId = Guid.NewGuid();
        routes.Start(rideId, new GeoRoute("LineString",
        [
            [105.80, 21.02],
            [105.81, 21.03],
            [105.82, 21.02],
        ]));

        Assert.True(routes.TryAdvance(rideId, 20, out var step));
        Assert.True(step.MovedM > 0);
        Assert.InRange(step.Lng, 105.80, 105.81);
        Assert.NotNull(routes.Geometry(rideId));

        routes.Forget(rideId);
        Assert.Null(routes.Geometry(rideId));
        Assert.False(routes.TryAdvance(rideId, 20, out _));
    }

    [Fact]
    public void Straight_backup_keeps_the_start_and_end()
    {
        var route = PickupRoutes.Straight(21.02, 105.80, 21.03, 105.82);
        Assert.Equal(105.80, route.Coordinates[0][0]);
        Assert.Equal(21.02, route.Coordinates[0][1]);
        Assert.Equal(105.82, route.Coordinates[1][0]);
        Assert.Equal(21.03, route.Coordinates[1][1]);
    }

    [Fact]
    public void Roam_advances_along_the_street_route_instead_of_cutting_across_blocks()
    {
        var roam = new DriverRoam();
        var driverId = Guid.NewGuid();
        var rideId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        roam.Begin(driverId, 21.0300, 105.8400, rideId, riderId);

        var pending = Assert.Single(roam.PendingRoutes());
        Assert.Equal(driverId, pending.DriverId);

        var streetRoute = new GeoRoute("LineString",
        [
            [105.8400, 21.0300],
            [105.8400, 21.0310],
            [105.8420, 21.0310],
        ]);
        roam.SetRoute(driverId, pending.Ticket, streetRoute);
        Assert.Empty(roam.PendingRoutes());

        Assert.True(roam.TryAdvance(driverId, 21.0300, 105.8400, 20, out var first));
        Assert.Equal(105.8400, first.Lng, 5);
        Assert.InRange(first.Lat, 21.0301, 21.0303);
        Assert.False(first.Finished);
        Assert.Equal(rideId, first.RideId);
        Assert.Equal(riderId, first.RiderId);

        for (var i = 0; i < 20 && !first.Finished; i++)
            Assert.True(roam.TryAdvance(driverId, first.Lat, first.Lng, 20, out first));

        Assert.True(first.Finished);
        Assert.Equal(21.0310, first.Lat, 4);
        Assert.Equal(105.8420, first.Lng, 4);
    }
}
