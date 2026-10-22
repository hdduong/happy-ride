using GrabBike.Api.Features.Drivers;
using GrabBike.Api.SharedKernel;

namespace GrabBike.Api.Tests;

public class PickupRouteTests
{
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
}
