namespace Ignis.Api.Integrations;

/// <summary>
/// Konfigurasi layanan luar (bagian "Integrations" di appsettings, atau env Integrations__FirmsKey dst.). Dibaca di server, bukan di klien.
/// Cara mendapatkan key: docs/integrasi-data.md.
/// </summary>
public sealed class IntegrationOptions
{
    // ---- NASA FIRMS (https://firms.modaps.eosdis.nasa.gov/api/area/) ----
    public string FirmsEndpoint { get; set; } = "https://firms.modaps.eosdis.nasa.gov/api/area/csv";

    /// <summary>MAP_KEY dari FIRMS. Kosong = sinkronisasi hotspot dilewati.</summary>
    public string FirmsKey { get; set; } = string.Empty;

    /// <summary>Alamat data_availability FIRMS (tanggal data terakhir per sumber); dipakai untuk menjelaskan hasil kosong.</summary>
    public string FirmsAvailabilityEndpoint { get; set; } = "https://firms.modaps.eosdis.nasa.gov/api/data_availability/csv";

    /// <summary>
    /// Sumber data dipisah koma. Default: tiga satelit VIIRS near-real-time. Suomi NPP diletakkan terakhir karena pada pemeriksaan
    /// 2026-11-21 FIRMS melaporkan data NRT-nya berhenti di 2026-11-02 (NOAA-20 dan NOAA-21 sampai hari ini).
    /// Nilai lain yang valid menurut dokumentasi FIRMS: MODIS_NRT, VIIRS_*_SP (data standar), dll.
    /// </summary>
    public string FirmsSources { get; set; } = "VIIRS_NOAA20_NRT,VIIRS_NOAA21_NRT,VIIRS_SNPP_NRT";

    /// <summary>Kotak area "barat,selatan,timur,utara" (derajat). Default: Indonesia.</summary>
    public string FirmsArea { get; set; } = "94,-11,141,6";

    /// <summary>
    /// Jumlah hari per permintaan (dokumentasi FIRMS: 1 sampai 5). PENTING: tanpa parameter tanggal, 1 hari hanya mencakup tanggal UTC
    /// hari ini, bukan 24 jam terakhir. Teramati: NOAA-20 untuk Indonesia = 0 baris (1 hari) lawan 2.382 baris (5 hari). Default 2
    /// (kemarin + hari ini, UTC) agar selalu mencakup 24 jam terakhir; duplikat antar sinkronisasi dibuang saat impor.
    /// </summary>
    public int FirmsDayRange { get; set; } = 2;

    /// <summary>Batas baris yang diimpor per sumber per sinkronisasi; bila lebih, yang keyakinan dan kecerahannya tertinggi dipilih.</summary>
    public int FirmsMaxRows { get; set; } = 3000;

    // ---- OpenWeather (https://openweathermap.org/current) ----
    public string WeatherEndpoint { get; set; } = "https://api.openweathermap.org/data/2.5/weather";

    /// <summary>API key OpenWeather. Bila kosong dicoba env OPENWEATHER_API_KEY (lihat WeatherClient).</summary>
    public string WeatherKey { get; set; } = string.Empty;

    /// <summary>
    /// Cuaca tersimpan dianggap segar selama ini (menit); setelah itu diambil ulang dari OpenWeather.
    /// OpenWeather menyarankan tidak memanggil lebih sering dari sekali per 10 menit per lokasi.
    /// </summary>
    public int WeatherMaxAgeMinutes { get; set; } = 60;

    // ---- Percobaan ulang (hanya untuk gangguan sementara: timeout, jaringan, HTTP 5xx) ----
    public int RetryCount { get; set; } = 2;

    /// <summary>Jeda sebelum percobaan ulang pertama (detik); berlipat dua tiap percobaan berikutnya.</summary>
    public int RetryDelaySeconds { get; set; } = 3;

    /// <summary>Daftar sumber FIRMS yang sudah dibersihkan dari spasi, kosong, dan duplikat.</summary>
    public IReadOnlyList<string> FirmsSourceList =>
        FirmsSources.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
}
