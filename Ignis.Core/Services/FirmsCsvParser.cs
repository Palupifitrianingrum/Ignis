using System.Globalization;
using Ignis.Core.Common;
using Ignis.Core.Entities;
using Ignis.Core.Enums;

namespace Ignis.Core.Services;

/// <summary>Mengurai CSV NASA FIRMS menjadi <see cref="SatelliteHotspot"/> (Id masih kosong). Fungsi murni, mudah diuji.</summary>
public static class FirmsCsvParser
{
    public const int MaxRows = 500;

    public static IReadOnlyList<SatelliteHotspot> Parse(string csv)
    {
        var result = new List<SatelliteHotspot>();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return result;

        var head = lines[0].Split(',').ToList();
        int iLat = head.IndexOf("latitude"), iLon = head.IndexOf("longitude");
        int iBr = head.IndexOf("bright_ti4"); if (iBr < 0) iBr = head.IndexOf("brightness");
        int iD = head.IndexOf("acq_date"), iT = head.IndexOf("acq_time");
        int iSat = head.IndexOf("satellite"), iIns = head.IndexOf("instrument"), iC = head.IndexOf("confidence");
        if (iLat < 0 || iLon < 0 || iD < 0 || iT < 0) return result;

        var inv = CultureInfo.InvariantCulture;
        foreach (var line in lines.Skip(1).Take(MaxRows))
        {
            var c = line.Split(',');
            if (c.Length <= Math.Max(iD, iT)) continue;
            if (!double.TryParse(c[iLat], NumberStyles.Float, inv, out var lat) ||
                !double.TryParse(c[iLon], NumberStyles.Float, inv, out var lon)) continue;
            if (!DateTime.TryParse(c[iD], inv, DateTimeStyles.None, out var day)) continue;
            var t = c[iT].PadLeft(4, '0');
            if (!int.TryParse(t[..2], out var hh) || !int.TryParse(t[2..4], out var mm)) continue;

            double br = 0;
            if (iBr >= 0) double.TryParse(c[iBr], NumberStyles.Float, inv, out br);

            var confidence = iC >= 0 && double.TryParse(c[iC], NumberStyles.Float, inv, out var cf) ? cf : 70;
            var satellite = iSat >= 0 && iSat < c.Length && !string.IsNullOrWhiteSpace(c[iSat]) ? c[iSat] : "VIIRS";
            var instrument = iIns >= 0 && iIns < c.Length && !string.IsNullOrWhiteSpace(c[iIns]) ? c[iIns] : "VIIRS";

            try
            {
                // Konstruktor memvalidasi invarian (rentang lat/lon, confidence, dst.); baris tidak valid dilewati.
                result.Add(new SatelliteHotspot(lat, lon, confidence, br, day.AddHours(hh).AddMinutes(mm),
                                                satellite, instrument, HotspotStatus.Active));
            }
            catch (DomainException)
            {
                continue;
            }
        }
        return result;
    }
}
