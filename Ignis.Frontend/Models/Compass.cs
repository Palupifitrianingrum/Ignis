using System;

namespace Ignis.Frontend.Models;

/// <summary>Nama arah mata angin (8 arah) dalam bahasa Indonesia untuk derajat 0 = utara, 90 = timur.</summary>
public static class Compass
{
    private static readonly string[] Names =
        { "Utara", "Timur Laut", "Timur", "Tenggara", "Selatan", "Barat Daya", "Barat", "Barat Laut" };

    public static string Name(double degrees)
    {
        var d = ((degrees % 360) + 360) % 360;
        return Names[(int)Math.Round(d / 45.0) % 8];
    }
}
