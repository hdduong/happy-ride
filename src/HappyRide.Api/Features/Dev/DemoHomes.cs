namespace HappyRide.Api.Features.Dev;

public static class DemoHomes
{
    private static readonly Dictionary<Guid, (double Lat, double Lng)> Drivers = new()
    {
        [Guid.Parse("33333333-3333-3333-3333-333333333333")] = (10.7735, 106.6990),
        [Guid.Parse("44444444-4444-4444-4444-444444444444")] = (10.7705, 106.6965),
        [Guid.Parse("55555555-5555-5555-5555-555555555555")] = (10.7740, 106.7010),
        [Guid.Parse("66666666-6666-6666-6666-666666666666")] = (10.7690, 106.7000),
        [Guid.Parse("99999999-9999-9999-9999-999999999999")] = (21.0352316, 105.8209560),
        [Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")] = (21.0303473, 105.8143142),
        [Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")] = (21.03050, 105.85080),
        [Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")] = (21.02680, 105.85430),
    };

    public static bool TryDriver(Guid userId, out double lat, out double lng)
    {
        if (!Drivers.TryGetValue(userId, out var home))
        {
            lat = 0;
            lng = 0;
            return false;
        }

        lat = home.Lat;
        lng = home.Lng;
        return true;
    }
}
