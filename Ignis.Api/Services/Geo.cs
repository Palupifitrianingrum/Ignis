namespace Ignis.Api.Services;

/// <summary>Perhitungan jarak dan arah geografis sederhana (cukup akurat untuk jarak puluhan kilometer).</summary>
public static class Geo
{
    public const double KmPerDegree = 111.32;
    private const double EarthRadiusKm = 6371.0088;

    private static readonly string[] CompassNames =
        { "Utara", "Timur Laut", "Timur", "Tenggara", "Selatan", "Barat Daya", "Barat", "Barat Laut" };

    /// <summary>Jarak lingkaran besar (haversine) antara dua titik, dalam km.</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = ToRad(lat2 - lat1), dLon = ToRad(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>
    /// Jarak (km) dari sebuah titik ke tepi terdekat sebuah sel persegi (nol bila titik di dalam sel).
    /// Sel dinyatakan lewat titik tengah dan setengah sisinya dalam derajat. Memakai pendekatan bidang lokal, cukup untuk skala sel simulasi.
    /// </summary>
    public static double DistanceToCellKm(double pointLat, double pointLon, double cellLat, double cellLon, double halfStepDegrees)
    {
        double cosLat = Math.Max(0.01, Math.Cos(ToRad((pointLat + cellLat) / 2)));
        double north = Math.Abs(cellLat - pointLat) * KmPerDegree;
        double east = Math.Abs(cellLon - pointLon) * KmPerDegree * cosLat;
        double halfNorth = halfStepDegrees * KmPerDegree;
        double halfEast = halfNorth * cosLat;
        double dy = Math.Max(0, north - halfNorth);
        double dx = Math.Max(0, east - halfEast);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Nama arah mata angin (8 arah) dalam bahasa Indonesia; 0 = utara, 90 = timur.</summary>
    public static string CompassName(double degrees)
    {
        var d = ((degrees % 360) + 360) % 360;
        return CompassNames[(int)Math.Round(d / 45.0) % 8];
    }

    private static double ToRad(double degrees) => degrees * Math.PI / 180.0;
}
