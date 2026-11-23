using System.Globalization;
using Ignis.Api.Domain;
using Ignis.Shared;

namespace Ignis.Api.Integrations;

/// <summary>Hasil mengurai satu CSV FIRMS.</summary>
/// <param name="Hotspots">Hotspot valid (Id masih kosong), sudah dibatasi bila melebihi batas baris.</param>
/// <param name="TotalRows">Jumlah baris data di CSV.</param>
/// <param name="Invalid">Baris yang dilewati karena rusak atau melanggar aturan domain.</param>
/// <param name="Truncated">True bila hotspot valid melebihi batas dan hanya sebagian yang dipertahankan.</param>
public sealed record FirmsParseResult(IReadOnlyList<SatelliteHotspot> Hotspots, int TotalRows, int Invalid, bool Truncated);

/// <summary>
/// Mengurai CSV NASA FIRMS menjadi <see cref="SatelliteHotspot"/>. Fungsi murni, mudah diuji.
///
/// Sesuai dokumentasi atribut FIRMS (earthdata.nasa.gov, "Active Fire Data Attributes for MODIS and VIIRS"):
///  - <c>acq_time</c> adalah waktu lintasan satelit dalam UTC (HHMM). Di sini diubah ke waktu lokal server,
///    karena seluruh aplikasi menyimpan waktu lokal (lihat TODO B10 soal zona waktu).
///  - VIIRS: <c>confidence</c> berupa kategori low/nominal/high (l/n/h), BUKAN angka; MODIS: angka 0-100.
///    Kategori dipetakan ke skala aplikasi (0-100) dengan konvensi kami sendiri: l=30, n=60, h=90.
///  - VIIRS: <c>bright_ti4</c> (Kelvin) adalah kecerahan; MODIS: <c>brightness</c>. Kolom <c>instrument</c> ada di CSV Area API (teramati)
///    tetapi tidak di file publik data_active_fire; bila tidak ada, diisi VIIRS/MODIS menurut kolom kecerahan.
///  - <c>satellite</c> berupa kode: N = Suomi NPP, N20 = NOAA-20, N21 = NOAA-21, A = Aqua, T = Terra.
///  - <c>acq_time</c> tidak selalu 4 digit (teramati "357" untuk 03:57); diberi nol di depan.
/// </summary>
public static class FirmsCsvParser
{
    public const int DefaultMaxRows = 3000;

    private static readonly Dictionary<string, string> SatelliteNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["N"] = "Suomi NPP", ["N20"] = "NOAA-20", ["N21"] = "NOAA-21", ["A"] = "Aqua", ["T"] = "Terra"
    };

    public static IReadOnlyList<SatelliteHotspot> Parse(string csv, int maxRows = DefaultMaxRows) =>
        ParseDetailed(csv, maxRows).Hotspots;

    public static FirmsParseResult ParseDetailed(string csv, int maxRows = DefaultMaxRows)
    {
        var empty = new FirmsParseResult(Array.Empty<SatelliteHotspot>(), 0, 0, false);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return empty;

        var head = lines[0].TrimStart('﻿').Split(',').Select(h => h.Trim().ToLowerInvariant()).ToList();
        int iLat = head.IndexOf("latitude"), iLon = head.IndexOf("longitude");
        int iD = head.IndexOf("acq_date"), iT = head.IndexOf("acq_time");
        if (iLat < 0 || iLon < 0 || iD < 0 || iT < 0) return empty;

        int iBr = head.IndexOf("bright_ti4"); if (iBr < 0) iBr = head.IndexOf("brightness");
        int iSat = head.IndexOf("satellite"), iIns = head.IndexOf("instrument"), iC = head.IndexOf("confidence");
        var defaultInstrument = head.Contains("bright_ti4") ? "VIIRS" : "MODIS";

        var inv = CultureInfo.InvariantCulture;
        var valid = new List<SatelliteHotspot>();
        int total = 0, invalid = 0;

        foreach (var line in lines.Skip(1))
        {
            total++;
            var c = line.Split(',');
            if (c.Length <= Math.Max(iD, iT) || c.Length <= Math.Max(iLat, iLon))
            {
                invalid++;
                continue;
            }

            if (!double.TryParse(c[iLat], NumberStyles.Float, inv, out var lat) ||
                !double.TryParse(c[iLon], NumberStyles.Float, inv, out var lon) ||
                !DateTime.TryParse(c[iD], inv, DateTimeStyles.None, out var day) ||
                !TryParseAcqTime(c[iT], out var hh, out var mm))
            {
                invalid++;
                continue;
            }

            double brightness = 0;
            if (iBr >= 0 && iBr < c.Length) double.TryParse(c[iBr], NumberStyles.Float, inv, out brightness);

            var rawSat = iSat >= 0 && iSat < c.Length ? c[iSat].Trim() : string.Empty;
            var satellite = rawSat.Length == 0 ? "VIIRS" : SatelliteNames.GetValueOrDefault(rawSat, rawSat);
            var instrument = iIns >= 0 && iIns < c.Length && !string.IsNullOrWhiteSpace(c[iIns]) ? c[iIns].Trim() : defaultInstrument;

            try
            {
                // Konstruktor memvalidasi invarian (rentang lat/lon, confidence, dst.); baris tidak valid dilewati.
                valid.Add(new SatelliteHotspot(
                    lat, lon, ParseConfidence(iC >= 0 && iC < c.Length ? c[iC] : null), brightness,
                    UtcToLocal(day.Date.AddHours(hh).AddMinutes(mm)), satellite, instrument, HotspotStatus.Active));
            }
            catch (DomainException)
            {
                invalid++;
            }
        }

        // Melebihi batas: pertahankan yang paling meyakinkan, bukan sekadar yang kebetulan ada di awal file.
        bool truncated = maxRows > 0 && valid.Count > maxRows;
        if (truncated)
            valid = valid.OrderByDescending(h => h.Confidence).ThenByDescending(h => h.Brightness).Take(maxRows).ToList();

        return new FirmsParseResult(valid, total, invalid, truncated);
    }

    /// <summary>"915" -> 09:15, "5" -> 00:05, "0915" -> 09:15. Menolak jam di luar 00-23 atau menit di luar 00-59.</summary>
    private static bool TryParseAcqTime(string raw, out int hours, out int minutes)
    {
        hours = minutes = 0;
        var t = raw.Trim().PadLeft(4, '0');
        return t.Length == 4
            && int.TryParse(t[..2], NumberStyles.None, CultureInfo.InvariantCulture, out hours)
            && int.TryParse(t[2..], NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
            && hours < 24 && minutes < 60;
    }

    /// <summary>acq_time FIRMS adalah UTC; aplikasi menyimpan waktu lokal server (Kind Unspecified, sesuai kolom timestamp tanpa zona).</summary>
    internal static DateTime UtcToLocal(DateTime utcWithoutKind) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcWithoutKind, DateTimeKind.Utc), TimeZoneInfo.Local),
            DateTimeKind.Unspecified);

    /// <summary>MODIS: angka 0-100. VIIRS: low/nominal/high (l/n/h). Tidak dikenal: dianggap nominal.</summary>
    internal static double ParseConfidence(string? raw)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !double.IsNaN(v))
            return Math.Clamp(v, 0, 100);

        return raw?.Trim().ToLowerInvariant() switch
        {
            "l" or "low" => 30,
            "n" or "nominal" => 60,
            "h" or "high" => 90,
            _ => 60
        };
    }
}
