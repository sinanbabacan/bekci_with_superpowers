namespace Bekci.Domain;

public static class ScanValidation
{
    private const double EarthRadiusMeters = 6_371_000;

    public static bool IsWithinGeofence(double cpLat, double cpLng, double radiusM, double? lat, double? lng)
    {
        if (lat is null || lng is null)
            return false;

        return DistanceMeters(cpLat, cpLng, lat.Value, lng.Value) <= radiusM;
    }

    public static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
    {
        double ToRad(double d) => d * Math.PI / 180.0;

        var dLat = ToRad(lat2 - lat1);
        var dLng = ToRad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }
}
