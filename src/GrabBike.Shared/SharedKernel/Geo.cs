namespace GrabBike.Api.SharedKernel;

public static class Geo
{
    private const double EarthRadiusM = 6_371_000;

    public static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLng = DegreesToRadians(lng2 - lng1);
        var lat1R = DegreesToRadians(lat1);
        var lat2R = DegreesToRadians(lat2);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1R) * Math.Cos(lat2R) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusM * c;
    }

    public static double BearingDegrees(double lat1, double lng1, double lat2, double lng2)
    {
        var lat1R = DegreesToRadians(lat1);
        var lat2R = DegreesToRadians(lat2);
        var dLng = DegreesToRadians(lng2 - lng1);
        var y = Math.Sin(dLng) * Math.Cos(lat2R);
        var x = Math.Cos(lat1R) * Math.Sin(lat2R) - Math.Sin(lat1R) * Math.Cos(lat2R) * Math.Cos(dLng);
        var bearing = Math.Atan2(y, x) * 180 / Math.PI;
        return (bearing + 360) % 360;
    }

    public static (double Lat, double Lng, double Heading, double MovedM) MoveToward(
        double lat,
        double lng,
        double targetLat,
        double targetLng,
        double stepM)
    {
        var distance = HaversineMeters(lat, lng, targetLat, targetLng);
        if (distance <= 1 || stepM <= 0)
            return (targetLat, targetLng, 0, 0);

        var take = Math.Min(stepM, distance);
        var fraction = take / distance;
        var nextLat = lat + (targetLat - lat) * fraction;
        var nextLng = lng + (targetLng - lng) * fraction;
        var heading = BearingDegrees(lat, lng, nextLat, nextLng);
        return (nextLat, nextLng, heading, take);
    }

    public static (double Lat, double Lng, double Heading, double MovedM, int Segment, double OffsetM) AdvanceRoute(
        double[][] coordinates,
        int segment,
        double offsetM,
        double stepM)
    {
        if (coordinates.Length == 0)
            return (0, 0, 0, 0, 0, 0);

        if (segment < 0)
            segment = 0;

        if (segment >= coordinates.Length - 1)
        {
            var end = coordinates[^1];
            return (end[1], end[0], 0, 0, Math.Max(coordinates.Length - 1, 0), 0);
        }

        var remaining = stepM;
        var heading = 0d;
        var moved = 0d;
        var lat = coordinates[segment][1];
        var lng = coordinates[segment][0];

        while (segment < coordinates.Length - 1 && remaining > 0.01)
        {
            var startLng = coordinates[segment][0];
            var startLat = coordinates[segment][1];
            var endLng = coordinates[segment + 1][0];
            var endLat = coordinates[segment + 1][1];
            var segmentLength = HaversineMeters(startLat, startLng, endLat, endLng);
            if (segmentLength < 0.5 || offsetM >= segmentLength - 0.01)
            {
                segment++;
                offsetM = 0;
                lat = endLat;
                lng = endLng;
                continue;
            }

            var available = segmentLength - offsetM;
            var take = Math.Min(available, remaining);
            var startFraction = offsetM / segmentLength;
            var endFraction = (offsetM + take) / segmentLength;
            var fromLat = startLat + (endLat - startLat) * startFraction;
            var fromLng = startLng + (endLng - startLng) * startFraction;
            lat = startLat + (endLat - startLat) * endFraction;
            lng = startLng + (endLng - startLng) * endFraction;
            heading = BearingDegrees(fromLat, fromLng, lat, lng);
            moved += take;
            remaining -= take;
            offsetM += take;
            if (offsetM >= segmentLength - 0.01)
            {
                segment++;
                offsetM = 0;
            }
        }

        return (lat, lng, heading, moved, segment, offsetM);
    }

    public static (double Lat, double Lng) OffsetMeters(double lat, double lng, double bearingDegrees, double distanceM)
    {
        var bearing = DegreesToRadians(bearingDegrees);
        var latR = DegreesToRadians(lat);
        var angular = distanceM / EarthRadiusM;
        var nextLat = Math.Asin(
            Math.Sin(latR) * Math.Cos(angular) +
            Math.Cos(latR) * Math.Sin(angular) * Math.Cos(bearing));
        var nextLng = DegreesToRadians(lng) + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angular) * Math.Cos(latR),
            Math.Cos(angular) - Math.Sin(latR) * Math.Sin(nextLat));
        var lngDegrees = nextLng * 180d / Math.PI;
        lngDegrees = (lngDegrees + 540d) % 360d - 180d;
        return (nextLat * 180d / Math.PI, lngDegrees);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
