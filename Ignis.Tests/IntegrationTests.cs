using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ignis.Api.Data;
using Ignis.Api.Domain;
using Ignis.Api.Integrations;
using Ignis.Api.Services;
using Ignis.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Ignis.Tests;

// =====================================================================================================================
// Tes integrasi NASA FIRMS dan OpenWeather. Semua memakai HTTP palsu (tanpa key, tanpa internet).
//
// Catatan jujur tentang data uji: format CSV dan JSON di bawah mengikuti dokumentasi resmi dan header CSV VIIRS yang teramati
// langsung dari server FIRMS. Baris datanya buatan sendiri (file publik FIRMS yang dicoba saat penulisan hanya berisi header),
// jadi bukti terhadap data nyata baru ada setelah key dipasang dan sinkronisasi dijalankan sungguhan.
// =====================================================================================================================

/// <summary>Handler HTTP palsu: jawaban diantre berurutan; bila antrean habis, jawaban terakhir diulang.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _queue = new();
    private Func<HttpResponseMessage>? _last;

    public List<string> Urls { get; } = new();
    public int Calls => Urls.Count;

    public FakeHandler Enqueue(HttpStatusCode status, string body = "", string contentType = "text/plain") =>
        Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, contentType) });

    public FakeHandler Enqueue(Func<HttpResponseMessage> make)
    {
        _queue.Enqueue(make);
        return this;
    }

    public FakeHandler EnqueueThrow(Exception ex) => Enqueue(() => throw ex);

    /// <summary>Kosongkan antrean (dan jawaban terakhir) untuk memulai skenario baru; riwayat URL dipertahankan.</summary>
    public FakeHandler Reset()
    {
        _queue.Clear();
        _last = null;
        return this;
    }

    /// <summary>Jawaban berbeda per sumber FIRMS, dikenali dari path URL.</summary>
    public Dictionary<string, Func<HttpResponseMessage>> BySource { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        Urls.Add(url);

        foreach (var (source, make) in BySource)
            if (url.Contains("/" + source + "/", StringComparison.Ordinal) || url.EndsWith("/" + source, StringComparison.Ordinal))
                return Task.FromResult(make());

        if (_queue.Count > 0) _last = _queue.Dequeue();
        return Task.FromResult((_last ?? throw new InvalidOperationException("FakeHandler: tidak ada jawaban yang disiapkan"))());
    }
}

internal static class Samples
{
    /// <summary>Header CSV VIIRS dari Area API FIRMS, persis seperti yang dikirim server (teramati langsung dengan key sungguhan, 2026-11-21).</summary>
    public const string ViirsHeader = "latitude,longitude,bright_ti4,scan,track,acq_date,acq_time,satellite,instrument,confidence,version,bright_ti5,frp,daynight";

    /// <summary>Header file publik data_active_fire (teramati): tanpa kolom instrument.</summary>
    public const string ViirsHeaderPublicFile = "latitude,longitude,bright_ti4,scan,track,acq_date,acq_time,satellite,confidence,version,bright_ti5,frp,daynight";

    public const string ModisHeader = "latitude,longitude,brightness,scan,track,acq_date,acq_time,satellite,instrument,confidence,version,bright_t31,frp,daynight";

    public static string ViirsRow(double lat, double lon, string time = "0915", string conf = "n", double bright = 342.5, string sat = "N", string date = "2026-10-24") =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{lat},{lon},{bright},0.4,0.4,{date},{time},{sat},VIIRS,{conf},2.0NRT,290.1,5.2,D");

    public static string Csv(params string[] rows) => ViirsHeader + "\n" + string.Join("\n", rows) + (rows.Length > 0 ? "\n" : "");

    /// <summary>Jawaban Current Weather dengan struktur sesuai dokumentasi OpenWeather (units=metric).</summary>
    public const string WeatherJson = """
        {"coord":{"lon":101.85,"lat":0.32},"weather":[{"id":500,"main":"Rain","description":"hujan ringan","icon":"10d"}],"base":"stations","main":{"temp":29.7,"feels_like":33.1,"temp_min":28.9,"temp_max":30.4,"pressure":1009,"humidity":71,"sea_level":1009,"grnd_level":1004},"visibility":10000,"wind":{"speed":3.6,"deg":250,"gust":6.2},"rain":{"1h":0.9},"clouds":{"all":75},"dt":1700000000,"name":"Riau","cod":200}
        """;

    public static IntegrationOptions Options(string? firmsKey = "KUNCI-RAHASIA-123", string? weatherKey = "KUNCI-CUACA-456") => new()
    {
        FirmsKey = firmsKey ?? "",
        WeatherKey = weatherKey ?? "",
        FirmsSources = "S1,S2",
        RetryCount = 2,
        RetryDelaySeconds = 0   // tes tidak boleh menunggu
    };
}

// ------------------------------------------------------------------------------------------------ parser CSV

public class FirmsCsvParserTests
{
    private static DateTime UtcToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.Local);

    [Fact]
    public void ViirsRow_IsReadAccordingToDocs()
    {
        var h = Assert.Single(FirmsCsvParser.Parse(Samples.Csv(Samples.ViirsRow(-0.39, 102.06, time: "0915", conf: "n", bright: 342.5))));

        Assert.Equal(-0.39, h.Latitude, 5);
        Assert.Equal(102.06, h.Longitude, 5);
        Assert.Equal(342.5, h.Brightness, 3);       // bright_ti4 (Kelvin)
        Assert.Equal("Suomi NPP", h.SatelliteName); // kode "N" diterjemahkan
        Assert.Equal("VIIRS", h.Instrument);        // CSV VIIRS tidak punya kolom instrument
        Assert.Equal(60, h.Confidence);             // nominal
        Assert.Equal(HotspotStatus.Active, h.Status);
    }

    // Baris nyata dari Area API FIRMS (NOAA-20, Papua, teramati 2026-11-21 dengan key sungguhan). Data publik, tanpa data pribadi.
    [Fact]
    public void RealRowsFromFirms_AreParsedCorrectly()
    {
        var csv = Samples.ViirsHeader + "\n" +
                  "-9.03097,140.95992,330.85,0.52,0.41,2026-11-17,357,N20,VIIRS,n,2.0NRT,298.27,3.25,D\n" +
                  "-8.95923,140.81593,335.14,0.53,0.42,2026-11-17,357,N20,VIIRS,l,2.0NRT,301.03,17.82,D\n";

        var rows = FirmsCsvParser.ParseDetailed(csv);

        Assert.Equal(2, rows.Hotspots.Count);
        Assert.Equal(0, rows.Invalid);
        var first = rows.Hotspots[0];
        Assert.Equal(-9.03097, first.Latitude, 5);
        Assert.Equal(140.95992, first.Longitude, 5);
        Assert.Equal(330.85, first.Brightness, 2);
        Assert.Equal("NOAA-20", first.SatelliteName);
        Assert.Equal("VIIRS", first.Instrument);
        Assert.Equal(60, first.Confidence);                 // n
        Assert.Equal(30, rows.Hotspots[1].Confidence);      // l
        // acq_time "357" = 03:57 UTC (tanpa nol di depan)
        Assert.Equal(UtcToLocal(new DateTime(2026, 11, 17, 3, 57, 0)), first.ScanTime);
    }

    [Fact]
    public void PublicFileHeader_WithoutInstrumentColumn_StillWorks()
    {
        var csv = Samples.ViirsHeaderPublicFile + "\n-0.39,102.06,342.5,0.4,0.4,2026-10-24,0915,N,n,2.0NRT,290,5,D\n";
        var h = Assert.Single(FirmsCsvParser.Parse(csv));
        Assert.Equal("VIIRS", h.Instrument);   // diisi dari keberadaan bright_ti4
        Assert.Equal("Suomi NPP", h.SatelliteName);
    }

    [Fact]
    public void AcqTime_IsUtc_AndConvertedToLocalServerTime()
    {
        var h = Assert.Single(FirmsCsvParser.Parse(Samples.Csv(Samples.ViirsRow(1, 100, time: "0915"))));

        var utc = new DateTime(2026, 10, 24, 9, 15, 0);
        Assert.Equal(UtcToLocal(utc), h.ScanTime);
        Assert.Equal(h.ScanTime, h.DetectedAt);
        // Selisih persis sebesar offset zona lokal: bukti bahwa jam UTC tidak diperlakukan sebagai jam lokal.
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), h.ScanTime - utc);
        Assert.Equal(DateTimeKind.Unspecified, h.ScanTime.Kind);   // sesuai kolom "timestamp without time zone"
    }

    [Fact]
    public void AcqTime_CrossingMidnight_MovesTheDate()
    {
        // 23:30 UTC pada tanggal 24 harus jatuh pada tanggal 25 bila zona lokal di timur UTC (mis. WIB, UTC+7).
        var h = Assert.Single(FirmsCsvParser.Parse(Samples.Csv(Samples.ViirsRow(1, 100, time: "2330"))));
        Assert.Equal(UtcToLocal(new DateTime(2026, 10, 24, 23, 30, 0)), h.ScanTime);
    }

    [Theory]
    [InlineData("l", 30)]
    [InlineData("n", 60)]
    [InlineData("h", 90)]
    [InlineData("low", 30)]
    [InlineData("nominal", 60)]
    [InlineData("high", 90)]
    [InlineData("H", 90)]
    [InlineData("87", 87)]      // MODIS: angka 0-100
    [InlineData("120", 100)]    // dibatasi ke 100
    [InlineData("", 60)]        // tidak dikenal: nominal
    [InlineData("xyz", 60)]
    [InlineData("NaN", 60)]
    public void Confidence_CategoriesAndNumbers(string raw, double expected)
    {
        var h = Assert.Single(FirmsCsvParser.Parse(Samples.Csv(Samples.ViirsRow(1, 100, conf: raw))));
        Assert.Equal(expected, h.Confidence);
    }

    [Theory]
    [InlineData("N", "Suomi NPP")]
    [InlineData("N20", "NOAA-20")]
    [InlineData("N21", "NOAA-21")]
    [InlineData("A", "Aqua")]
    [InlineData("T", "Terra")]
    [InlineData("Z9", "Z9")]    // kode tak dikenal dipertahankan
    public void SatelliteCodes_AreTranslated(string code, string expected)
    {
        var h = Assert.Single(FirmsCsvParser.Parse(Samples.Csv(Samples.ViirsRow(1, 100, sat: code))));
        Assert.Equal(expected, h.SatelliteName);
    }

    [Fact]
    public void ModisRow_UsesBrightnessColumn_NumericConfidence_AndInstrument()
    {
        var csv = Samples.ModisHeader + "\n1.2,100.3,330.5,1,1,2026-10-24,1230,A,MODIS,85,6.1NRT,300,10,D\n";
        var h = Assert.Single(FirmsCsvParser.Parse(csv));

        Assert.Equal(330.5, h.Brightness, 3);
        Assert.Equal(85, h.Confidence);
        Assert.Equal("Aqua", h.SatelliteName);
        Assert.Equal("MODIS", h.Instrument);
    }

    [Fact]
    public void ShortAcqTime_IsPadded_AndBadRowsAreCountedAndSkipped()
    {
        var csv = Samples.Csv(
            Samples.ViirsRow(1, 100, time: "5"),        // 00:05
            Samples.ViirsRow(2, 100, time: "2561"),     // jam 25: tidak valid
            Samples.ViirsRow(3, 100, time: "0960"),     // menit 60: tidak valid
            Samples.ViirsRow(95, 100),                  // lintang di luar jangkauan: melanggar aturan domain
            "bad,row",                                  // kolom kurang
            "x,y,1,1,1,2026-10-24,0900,N,n,v,1,1,D");   // bukan angka

        var r = FirmsCsvParser.ParseDetailed(csv);

        Assert.Equal(6, r.TotalRows);
        Assert.Equal(5, r.Invalid);
        var h = Assert.Single(r.Hotspots);
        Assert.Equal(UtcToLocal(new DateTime(2026, 10, 24, 0, 5, 0)), h.ScanTime);
    }

    [Fact]
    public void HeaderOnly_Empty_AndMissingColumns_AreEmpty()
    {
        Assert.Empty(FirmsCsvParser.Parse(Samples.ViirsHeader + "\n"));   // jawaban sah tanpa kejadian
        Assert.Empty(FirmsCsvParser.Parse("foo,bar\n1,2\n"));
        Assert.Empty(FirmsCsvParser.Parse(""));
        Assert.Equal(0, FirmsCsvParser.ParseDetailed(Samples.ViirsHeader + "\n").TotalRows);
    }

    [Fact]
    public void WindowsLineEndings_AndByteOrderMark_AreHandled()
    {
        var csv = "﻿" + Samples.ViirsHeader + "\r\n" + Samples.ViirsRow(1, 100) + "\r\n" + Samples.ViirsRow(2, 101) + "\r\n";
        Assert.Equal(2, FirmsCsvParser.Parse(csv).Count);
    }

    [Fact]
    public void WhenOverLimit_KeepsMostConfident_NotJustTheFirstRows()
    {
        var csv = Samples.Csv(
            Samples.ViirsRow(1, 100, conf: "l", bright: 300),
            Samples.ViirsRow(2, 100, conf: "l", bright: 301),
            Samples.ViirsRow(3, 100, conf: "h", bright: 340),
            Samples.ViirsRow(4, 100, conf: "n", bright: 330),
            Samples.ViirsRow(5, 100, conf: "h", bright: 360));

        var r = FirmsCsvParser.ParseDetailed(csv, maxRows: 2);

        Assert.True(r.Truncated);
        Assert.Equal(5, r.TotalRows);
        Assert.Equal(new[] { 5.0, 3.0 }, r.Hotspots.Select(h => h.Latitude).ToArray());   // keduanya 'h', yang lebih terang dulu
    }

    [Fact]
    public void UnderLimit_IsNotTruncated() =>
        Assert.False(FirmsCsvParser.ParseDetailed(Samples.Csv(Samples.ViirsRow(1, 100)), maxRows: 5).Truncated);
}

// ------------------------------------------------------------------------------------------------ klien FIRMS

public class FirmsClientTests
{
    private const string Key = "KUNCI-RAHASIA-123";

    private static (FirmsClient client, FakeHandler handler) Make(string? key = Key, Action<IntegrationOptions>? tweak = null)
    {
        var handler = new FakeHandler();
        var options = Samples.Options(firmsKey: key);
        tweak?.Invoke(options);
        return (new FirmsClient(new HttpClient(handler), options), handler);
    }

    [Fact]
    public void BuildUrl_FollowsDocumentedPathOrder()
    {
        var (client, _) = Make();
        Assert.Equal($"https://firms.modaps.eosdis.nasa.gov/api/area/csv/{Key}/VIIRS_SNPP_NRT/94,-11,141,6/2", client.BuildUrl("VIIRS_SNPP_NRT"));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(9, 5)]    // dokumentasi FIRMS: 1 sampai 5 hari
    public void BuildUrl_ClampsDayRange(int configured, int expected)
    {
        var (client, _) = Make(tweak: o => o.FirmsDayRange = configured);
        Assert.EndsWith($"/94,-11,141,6/{expected}", client.BuildUrl("VIIRS_SNPP_NRT"));
    }

    [Fact]
    public async Task Fetch_ReturnsCsv_AndCallsTheExpectedUrl()
    {
        var (client, handler) = Make();
        var csv = Samples.Csv(Samples.ViirsRow(1, 100));
        handler.Enqueue(HttpStatusCode.OK, csv);

        Assert.Equal(csv, await client.FetchCsvAsync("VIIRS_NOAA20_NRT"));
        Assert.Equal($"https://firms.modaps.eosdis.nasa.gov/api/area/csv/{Key}/VIIRS_NOAA20_NRT/94,-11,141,6/2", Assert.Single(handler.Urls));
    }

    [Fact]
    public async Task Fetch_HeaderOnly_IsAValidEmptyAnswer()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n");
        Assert.Contains("latitude", await client.FetchCsvAsync("S1"));
    }

    [Fact]
    public async Task MissingKey_ThrowsWithoutAnyHttpCall()
    {
        var (client, handler) = Make(key: "");
        var ex = await Assert.ThrowsAsync<IntegrationException>(() => client.FetchCsvAsync("S1"));
        Assert.Equal(IntegrationFailure.MissingKey, ex.Failure);
        Assert.Equal(0, handler.Calls);
        Assert.False(client.IsConfigured);
    }

    // Perilaku teramati langsung pada server FIRMS: key palsu -> HTTP 400 dengan teks "Invalid MAP_KEY."
    [Fact]
    public async Task InvalidKey_Http400_IsPermanent_AndMessageHasNoKey()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.BadRequest, "Invalid MAP_KEY.");

        var ex = await Assert.ThrowsAsync<IntegrationException>(() => client.FetchCsvAsync("S1"));

        Assert.Equal(IntegrationFailure.InvalidKey, ex.Failure);
        Assert.False(ex.IsTransient);
        Assert.Contains("Invalid MAP_KEY", ex.Message);
        Assert.DoesNotContain(Key, ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, IntegrationFailure.InvalidKey, false)]
    [InlineData(HttpStatusCode.Unauthorized, IntegrationFailure.InvalidKey, false)]
    [InlineData(HttpStatusCode.TooManyRequests, IntegrationFailure.RateLimited, false)]
    [InlineData(HttpStatusCode.InternalServerError, IntegrationFailure.Unavailable, true)]
    [InlineData(HttpStatusCode.BadGateway, IntegrationFailure.Unavailable, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, IntegrationFailure.Unavailable, true)]
    [InlineData(HttpStatusCode.NotFound, IntegrationFailure.BadResponse, false)]
    public async Task HttpStatuses_AreClassified(HttpStatusCode status, IntegrationFailure expected, bool transient)
    {
        var (client, handler) = Make();
        handler.Enqueue(status, "kesalahan apa pun");

        var ex = await Assert.ThrowsAsync<IntegrationException>(() => client.FetchCsvAsync("S1"));

        Assert.Equal(expected, ex.Failure);
        Assert.Equal(transient, ex.IsTransient);
        Assert.DoesNotContain(Key, ex.Message);
    }

    [Fact]
    public async Task ErrorBodyEchoingTheKey_IsRedacted()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.NotFound, $"tidak ada data untuk {Key} di sini");

        var ex = await Assert.ThrowsAsync<IntegrationException>(() => client.FetchCsvAsync("S1"));

        Assert.DoesNotContain(Key, ex.Message);
        Assert.Contains("***", ex.Message);
    }

    [Theory]
    [InlineData("<html><body>Error in processing</body></html>")]
    [InlineData("Error in processing:")]
    [InlineData("")]
    public async Task Http200ButNotCsv_IsABadResponse_NotAnEmptyImport(string body)
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK, body);

        var ex = await Assert.ThrowsAsync<IntegrationException>(() => client.FetchCsvAsync("S1"));
        Assert.Equal(IntegrationFailure.BadResponse, ex.Failure);
    }

    [Fact]
    public async Task NetworkFailure_IsTransient_AndMessageHasNoKey()
    {
        var (client, handler) = Make();
        handler.EnqueueThrow(new HttpRequestException($"gagal menyambung ke https://x/{Key}/y"));

        var ex = await Assert.ThrowsAsync<IntegrationException>(() => client.FetchCsvAsync("S1"));

        Assert.True(ex.IsTransient);
        Assert.DoesNotContain(Key, ex.Message);
    }

    [Fact]
    public async Task Timeout_IsTransient()
    {
        var (client, handler) = Make();
        handler.EnqueueThrow(new TaskCanceledException("timeout"));
        Assert.True((await Assert.ThrowsAsync<IntegrationException>(() => client.FetchCsvAsync("S1"))).IsTransient);
    }

    // Default 2 hari bukan kebetulan: tanpa tanggal, 1 hari hanya = tanggal UTC hari ini (teramati: 0 baris vs 2.382 baris untuk 5 hari).
    [Fact]
    public void Defaults_CoverTheLast24Hours_AndPutTheStalledSatelliteLast()
    {
        var defaults = new IntegrationOptions();
        Assert.Equal(2, defaults.FirmsDayRange);
        Assert.Equal(new[] { "VIIRS_NOAA20_NRT", "VIIRS_NOAA21_NRT", "VIIRS_SNPP_NRT" }, defaults.FirmsSourceList.ToArray());
    }

    [Fact]
    public async Task LatestAvailableDate_ReadsTheAvailabilityCsv_AndUsesTheDocumentedEndpoint()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK, "data_id,min_date,max_date\nVIIRS_SNPP_NRT,2026-08-01,2026-11-02\n");

        var date = await client.GetLatestAvailableDateAsync("VIIRS_SNPP_NRT");

        Assert.Equal(new DateOnly(2026, 11, 2), date);
        Assert.Equal($"https://firms.modaps.eosdis.nasa.gov/api/data_availability/csv/{Key}/VIIRS_SNPP_NRT", Assert.Single(handler.Urls));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Invalid MAP_KEY.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "x")]
    [InlineData(HttpStatusCode.OK, "bukan csv")]
    [InlineData(HttpStatusCode.OK, "")]
    public async Task LatestAvailableDate_NeverThrows_ReturnsNullWhenUnknown(HttpStatusCode status, string body)
    {
        var (client, handler) = Make();
        handler.Enqueue(status, body);
        Assert.Null(await client.GetLatestAvailableDateAsync("S1"));
    }

    [Fact]
    public async Task CheckConnection_UsesATinyArea_AndReportsStates()
    {
        var (client, handler) = Make();
        handler.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n");
        var ok = await client.CheckConnectionAsync();
        Assert.Equal(ConnectionState.Connected, ok.State);
        Assert.Contains("/101.7,0.2,101.9,0.4/1", Assert.Single(handler.Urls));   // 1 transaksi, data minim

        handler.Enqueue(HttpStatusCode.BadRequest, "Invalid MAP_KEY.");
        var bad = await client.CheckConnectionAsync();
        Assert.Equal(ConnectionState.Failed, bad.State);
        Assert.DoesNotContain(Key, bad.Message);

        var (noKey, _) = Make(key: "");
        Assert.Equal(ConnectionState.MissingKey, (await noKey.CheckConnectionAsync()).State);
    }
}

// ------------------------------------------------------------------------------------------------ klien OpenWeather

public class WeatherClientTests
{
    private const string Key = "KUNCI-CUACA-456";

    private static (WeatherClient client, FakeHandler handler, IntegrationStatus status) Make(string? key = Key)
    {
        var handler = new FakeHandler();
        var status = new IntegrationStatus();
        // WeatherKey kosong akan mencoba env OPENWEATHER_API_KEY; tes "tanpa key" tetap hermetik karena handler menghitung panggilan.
        return (new WeatherClient(new HttpClient(handler), Samples.Options(weatherKey: key), status), handler, status);
    }

    [Fact]
    public void Parse_ReadsDocumentedFields_InMetricUnits()
    {
        var w = WeatherClient.Parse(Samples.WeatherJson, 0.32, 101.85);

        Assert.Equal(29.7, w.Temperature);       // °C
        Assert.Equal(71, w.Humidity);            // %
        Assert.Equal(1009, w.Pressure);          // hPa
        Assert.Equal(3.6, w.WindSpeed);          // m/s
        Assert.Equal(250, w.WindDirection);      // arah ASAL angin (meteorologis)
        Assert.Equal(0.9, w.Precipitation);      // rain.1h, mm/jam
        Assert.Equal("hujan ringan", w.Condition);
        Assert.Equal(0.32, w.Latitude);
        Assert.Equal(101.85, w.Longitude);
    }

    [Fact]
    public void Parse_MissingOptionalBlocks_DefaultsToZero()
    {
        var w = WeatherClient.Parse("""{"main":{"temp":31,"humidity":55}}""", 1, 2);
        Assert.Equal(0, w.WindSpeed);
        Assert.Equal(0, w.WindDirection);
        Assert.Equal(0, w.Precipitation);
        Assert.Equal(0, w.Pressure);
        Assert.Equal(string.Empty, w.Condition);
    }

    [Theory]
    [InlineData("""{"main":{"humidity":55}}""")]    // tanpa temp
    [InlineData("""{"weather":[]}""")]             // tanpa main
    [InlineData("bukan json")]
    [InlineData("""{"main":{"temp":"panas","humidity":1}}""")]
    public void Parse_UnusableAnswer_IsABadResponse(string json)
    {
        var ex = Assert.Throws<IntegrationException>(() => WeatherClient.Parse(json, 1, 2));
        Assert.Equal(IntegrationFailure.BadResponse, ex.Failure);
    }

    [Fact]
    public async Task Request_UsesLatLon_MetricUnits_InvariantNumbers_AndTheKey()
    {
        var (client, handler, _) = Make();
        handler.Enqueue(HttpStatusCode.OK, Samples.WeatherJson, "application/json");

        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("id-ID");   // koma desimal
        try { await client.TryFetchAsync(-2.9, 104.6); }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }

        var url = Assert.Single(handler.Urls);
        Assert.StartsWith("https://api.openweathermap.org/data/2.5/weather?", url);
        Assert.Contains("lat=-2.9&", url);          // titik, bukan koma
        Assert.Contains("lon=104.6&", url);
        Assert.Contains($"appid={Key}", url);
        Assert.Contains("units=metric", url);
    }

    [Fact]
    public async Task Success_ClearsPreviousError_AndRecordsTime()
    {
        var (client, handler, status) = Make();
        status.WeatherFailed("galat lama");
        handler.Enqueue(HttpStatusCode.OK, Samples.WeatherJson, "application/json");

        Assert.NotNull(await client.TryFetchAsync(0.32, 101.85));
        Assert.Null(status.WeatherLastError);
        Assert.NotNull(status.WeatherLastSuccess);
    }

    [Fact]
    public async Task NoKey_ReturnsNull_WithoutHttp_AndFallsBackToSimulation()
    {
        var (client, handler, _) = Make(key: "");
        if (client.IsConfigured) return;   // mesin ini punya OPENWEATHER_API_KEY di environment; tes ini tidak relevan di sana

        Assert.Null(await client.TryFetchAsync(1, 2));
        Assert.Equal("Simulasi", (await client.GetWeatherAsync(1, 2)).Condition);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Http401_FallsBackToSimulation_AndRecordsAHelpfulReason()
    {
        var (client, handler, status) = Make();
        handler.Enqueue(HttpStatusCode.Unauthorized, """{"cod":401,"message":"Invalid API key."}""", "application/json");

        Assert.Null(await client.TryFetchAsync(1, 2));
        Assert.Contains("belum aktif", status.WeatherLastError);   // key baru bisa butuh sampai 2 jam
        Assert.DoesNotContain(Key, status.WeatherLastError);

        Assert.Equal("Simulasi", (await client.GetWeatherAsync(1, 2)).Condition);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "503")]
    [InlineData(HttpStatusCode.OK, "tidak dapat dibaca")]
    public async Task OtherFailures_AreRecorded(HttpStatusCode status, string expectedFragment)
    {
        var (client, handler, st) = Make();
        handler.Enqueue(status, status == HttpStatusCode.OK ? "<html>bukan json</html>" : "x");

        Assert.Null(await client.TryFetchAsync(1, 2));
        Assert.Contains(expectedFragment, st.WeatherLastError);
    }

    [Fact]
    public async Task CheckConnection_ReportsStates_AndNeverLeaksTheKey()
    {
        var (client, handler, _) = Make();
        handler.Enqueue(HttpStatusCode.OK, Samples.WeatherJson, "application/json");
        var ok = await client.CheckConnectionAsync();
        Assert.Equal(ConnectionState.Connected, ok.State);
        Assert.Contains("lat=0.32&lon=101.85", Assert.Single(handler.Urls));   // koordinat, bukan geocoding nama kota (sudah usang)

        handler.Enqueue(HttpStatusCode.Unauthorized, "x");
        var bad = await client.CheckConnectionAsync();
        Assert.Equal(ConnectionState.Failed, bad.State);
        Assert.DoesNotContain(Key, bad.Message);

        var (noKey, _, _) = Make(key: "");
        if (!noKey.IsConfigured) Assert.Equal(ConnectionState.MissingKey, (await noKey.CheckConnectionAsync()).State);
    }
}

// ------------------------------------------------------------------------------------------------ sinkronisasi

public class SyncServiceTests
{
    private const string Key = "KUNCI-RAHASIA-123";

    private sealed class Rig
    {
        public required AppDbContext Db { get; init; }
        public required FakeHandler Firms { get; init; }
        public required IntegrationStatus Status { get; init; }
        public required SyncService Sync { get; init; }
        public required IntegrationOptions Options { get; init; }
    }

    private static Rig Make(string? firmsKey = Key, Action<IntegrationOptions>? tweak = null)
    {
        var db = TestDb.Create(seedDemo: false);
        var options = Samples.Options(firmsKey: firmsKey, weatherKey: "");
        tweak?.Invoke(options);
        var firmsHandler = new FakeHandler();
        var status = new IntegrationStatus();
        var log = new ActivityLogService(db);
        var weather = new WeatherClient(new HttpClient(new FakeHandler().Enqueue(HttpStatusCode.OK, Samples.WeatherJson)), options, status);
        var sync = new SyncService(new FirmsClient(new HttpClient(firmsHandler), options), weather, new HotspotService(db, log), options, log, status);
        return new Rig { Db = db, Firms = firmsHandler, Status = status, Sync = sync, Options = options };
    }

    private static string CsvOf(int count, double latStart = 1) =>
        Samples.Csv(Enumerable.Range(0, count).Select(i => Samples.ViirsRow(latStart + i * 0.01, 100 + i * 0.01)).ToArray());

    [Fact]
    public async Task NoKey_IsSkipped_WithoutHttp()
    {
        var rig = Make(firmsKey: "");
        var result = await rig.Sync.RunAsync();

        Assert.Equal(ConnectionState.MissingKey, result.Firms);
        Assert.Equal(0, rig.Firms.Calls);
        Assert.Contains(result.Messages, m => m.Contains("API key belum diisi"));
    }

    [Fact]
    public async Task ImportsFromEverySource_AndIsIdempotent()
    {
        var rig = Make();
        rig.Firms.BySource["S1"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CsvOf(3, 1)) };
        rig.Firms.BySource["S2"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CsvOf(2, 5)) };

        var first = await rig.Sync.RunAsync();
        Assert.Equal(ConnectionState.Connected, first.Firms);
        Assert.Equal(5, first.NewHotspots);
        Assert.Contains(first.Messages, m => m.StartsWith("FIRMS S1: 3 baris, 3 hotspot baru"));
        Assert.Contains(first.Messages, m => m.StartsWith("FIRMS S2: 2 baris, 2 hotspot baru"));

        var stored = await rig.Db.Hotspots.OfType<SatelliteHotspot>().ToListAsync();
        Assert.Equal(5, stored.Count);
        Assert.Equal(5, stored.Select(h => h.Id).Distinct().Count());
        Assert.All(stored, h => Assert.StartsWith("HS-", h.Id));

        var second = await rig.Sync.RunAsync();   // data yang sama: tidak ada yang baru
        Assert.Equal(0, second.NewHotspots);
        Assert.Equal(5, await rig.Db.Hotspots.CountAsync());
    }

    [Fact]
    public async Task DuplicateRowsInsideOneCsv_AreImportedOnce()
    {
        var rig = Make(tweak: o => o.FirmsSources = "S1");
        var row = Samples.ViirsRow(1, 100);
        rig.Firms.Enqueue(HttpStatusCode.OK, Samples.Csv(row, row, row));

        Assert.Equal(1, (await rig.Sync.RunAsync()).NewHotspots);
    }

    [Fact]
    public async Task HotspotAlreadyInDatabase_IsNotImportedAgain()
    {
        var rig = Make(tweak: o => o.FirmsSources = "S1");
        var existing = FirmsCsvParser.Parse(Samples.Csv(Samples.ViirsRow(1, 100))).Single();
        existing.AssignId("HS-0001");
        rig.Db.Hotspots.Add(existing);
        await rig.Db.SaveChangesAsync();

        rig.Firms.Enqueue(HttpStatusCode.OK, Samples.Csv(Samples.ViirsRow(1, 100), Samples.ViirsRow(2, 100)));

        Assert.Equal(1, (await rig.Sync.RunAsync()).NewHotspots);
    }

    [Fact]
    public async Task InvalidKey_IsNotRetried_StopsOtherSources_AndNeverLeaksTheKey()
    {
        var rig = Make();
        rig.Firms.Enqueue(HttpStatusCode.BadRequest, "Invalid MAP_KEY.");

        var result = await rig.Sync.RunAsync();

        Assert.Equal(1, rig.Firms.Calls);   // tanpa ulang, dan sumber S2 tidak disentuh
        Assert.Equal(ConnectionState.Failed, result.Firms);
        Assert.Contains(result.Messages, m => m.Contains("Invalid MAP_KEY"));
        Assert.Contains(result.Messages, m => m.Contains("dilewati"));
        Assert.DoesNotContain(result.Messages, m => m.Contains(Key));
        Assert.DoesNotContain(await rig.Db.ActivityLog.Select(a => a.Message).ToListAsync(), m => m.Contains(Key));
    }

    [Fact]
    public async Task RateLimit_IsNotRetried()
    {
        var rig = Make();
        rig.Firms.Enqueue(HttpStatusCode.TooManyRequests, "terlalu banyak");

        var result = await rig.Sync.RunAsync();

        Assert.Equal(1, rig.Firms.Calls);
        Assert.Equal(ConnectionState.Failed, result.Firms);
    }

    [Fact]
    public async Task TransientErrors_AreRetried_ThenSucceed()
    {
        var rig = Make(tweak: o => o.FirmsSources = "S1");
        rig.Firms.Enqueue(HttpStatusCode.ServiceUnavailable)
                 .Enqueue(HttpStatusCode.BadGateway)
                 .Enqueue(HttpStatusCode.OK, CsvOf(2));

        var result = await rig.Sync.RunAsync();

        Assert.Equal(3, rig.Firms.Calls);   // 1 + RetryCount(2)
        Assert.Equal(ConnectionState.Connected, result.Firms);
        Assert.Equal(2, result.NewHotspots);
    }

    [Fact]
    public async Task TransientErrors_ExhaustRetries_AndSkipRemainingSources()
    {
        var rig = Make();   // S1,S2
        rig.Firms.Enqueue(HttpStatusCode.ServiceUnavailable);

        var result = await rig.Sync.RunAsync();

        Assert.Equal(3, rig.Firms.Calls);   // S1 dicoba 3 kali, S2 dilewati karena koneksi sudah bermasalah
        Assert.Equal(ConnectionState.Failed, result.Firms);
        Assert.Contains(result.Messages, m => m.Contains("S1 gagal"));
        Assert.Contains(result.Messages, m => m.Contains("dilewati"));
    }

    [Fact]
    public async Task OneSourceFailing_DoesNotLoseTheOthers()
    {
        var rig = Make();
        rig.Firms.BySource["S1"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CsvOf(2)) };
        rig.Firms.BySource["S2"] = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("x") };

        var result = await rig.Sync.RunAsync();

        Assert.Equal(ConnectionState.Connected, result.Firms);   // sebagian berhasil
        Assert.Equal(2, result.NewHotspots);
        Assert.Contains(result.Messages, m => m.Contains("S2 gagal"));
    }

    [Fact]
    public async Task EmptyResult_ExplainsWhenTheSourceLastHadData()
    {
        var rig = Make(tweak: o => o.FirmsSources = "S1");
        rig.Firms.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n")                                      // 0 baris
                 .Enqueue(HttpStatusCode.OK, "data_id,min_date,max_date\nS1,2026-08-01,2026-11-02\n");        // ketersediaan

        var result = await rig.Sync.RunAsync();

        Assert.Equal(ConnectionState.Connected, result.Firms);   // kosong itu sah, bukan kegagalan
        Assert.Equal(0, result.NewHotspots);
        Assert.Contains(result.Messages, m => m.Contains("0 baris") && m.Contains("data terakhir yang tersedia di FIRMS untuk sumber ini: 2026-11-02"));
        Assert.Equal(2, rig.Firms.Calls);
    }

    [Fact]
    public async Task EmptyResult_StillSucceeds_WhenTheAvailabilityLookupFails()
    {
        var rig = Make(tweak: o => o.FirmsSources = "S1");
        rig.Firms.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n").Enqueue(HttpStatusCode.ServiceUnavailable, "x");

        var result = await rig.Sync.RunAsync();

        Assert.Equal(ConnectionState.Connected, result.Firms);
        Assert.Contains(result.Messages, m => m.StartsWith("FIRMS S1: 0 baris, 0 hotspot baru") && !m.Contains("data terakhir"));
    }

    [Fact]
    public async Task ANonEmptyResult_DoesNotSpendAnExtraRequest()
    {
        var rig = Make(tweak: o => o.FirmsSources = "S1");
        rig.Firms.Enqueue(HttpStatusCode.OK, CsvOf(2));

        await rig.Sync.RunAsync();

        Assert.Equal(1, rig.Firms.Calls);
    }

    [Fact]
    public async Task Http200WithHtml_IsAnError_NotZeroNewHotspots()
    {
        var rig = Make(tweak: o => o.FirmsSources = "S1");
        rig.Firms.Enqueue(HttpStatusCode.OK, "<html>Error in processing</html>");

        var result = await rig.Sync.RunAsync();

        Assert.Equal(ConnectionState.Failed, result.Firms);
        Assert.Contains(result.Messages, m => m.Contains("bukan CSV"));
    }

    [Fact]
    public async Task OverTheRowLimit_ImportsTheMostConfident_AndSaysSo()
    {
        var rig = Make(tweak: o => { o.FirmsSources = "S1"; o.FirmsMaxRows = 2; });
        rig.Firms.Enqueue(HttpStatusCode.OK, Samples.Csv(
            Samples.ViirsRow(1, 100, conf: "l"), Samples.ViirsRow(2, 100, conf: "h"),
            Samples.ViirsRow(3, 100, conf: "n"), Samples.ViirsRow(4, 100, conf: "h", bright: 360)));

        var result = await rig.Sync.RunAsync();

        Assert.Equal(2, result.NewHotspots);
        Assert.Contains(result.Messages, m => m.Contains("dibatasi 2 baris"));
        var lats = (await rig.Db.Hotspots.ToListAsync()).Select(h => h.Latitude).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 2.0, 4.0 }, lats);
    }

    [Fact]
    public async Task InvalidRows_AreCountedInTheMessage()
    {
        var rig = Make(tweak: o => o.FirmsSources = "S1");
        rig.Firms.Enqueue(HttpStatusCode.OK, Samples.Csv(Samples.ViirsRow(1, 100), Samples.ViirsRow(95, 100), "bad,row"));

        var result = await rig.Sync.RunAsync();

        Assert.Equal(1, result.NewHotspots);
        Assert.Contains(result.Messages, m => m.Contains("2 baris tidak valid dilewati"));
    }

    [Fact]
    public async Task OnlyOneSyncRunsAtATime_AndTheGateIsReleasedAfterwards()
    {
        var rig = Make(tweak: o => o.FirmsSources = "S1");
        rig.Firms.Enqueue(HttpStatusCode.OK, CsvOf(1));

        Assert.True(rig.Status.TryBeginSync());   // anggap sinkronisasi lain sedang berjalan
        var blocked = await rig.Sync.RunAsync();
        Assert.Contains("sedang berjalan", blocked.Messages.Single());
        Assert.Equal(0, rig.Firms.Calls);

        rig.Status.EndSync();
        Assert.Equal(1, (await rig.Sync.RunAsync()).NewHotspots);
        Assert.True(rig.Status.TryBeginSync());   // gerbang dilepas setelah selesai
    }

    [Fact]
    public async Task GateIsReleased_EvenWhenTheSyncFails()
    {
        var rig = Make(tweak: o => o.FirmsSources = "S1");
        rig.Firms.Enqueue(HttpStatusCode.BadRequest, "Invalid MAP_KEY.");

        await rig.Sync.RunAsync();

        Assert.True(rig.Status.TryBeginSync());
    }

    [Fact]
    public async Task ThreeThousandRows_ImportQuickly_WithUniqueIds()
    {
        var rig = Make(tweak: o => { o.FirmsSources = "S1"; o.FirmsMaxRows = 5000; });
        rig.Firms.Enqueue(HttpStatusCode.OK, CsvOf(3000));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await rig.Sync.RunAsync();
        watch.Stop();

        Assert.Equal(3000, result.NewHotspots);
        Assert.Equal(3000, (await rig.Db.Hotspots.Select(h => h.Id).Distinct().CountAsync()));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"impor 3000 baris terlalu lama: {watch.Elapsed}");
    }

    [Fact]
    public async Task WeatherCheck_IsPartOfTheResult()
    {
        var rig = Make(firmsKey: "");
        var result = await rig.Sync.RunAsync();
        Assert.Contains(result.Messages, m => m.StartsWith("Weather"));
    }
}

// ------------------------------------------------------------------------------------------------ cuaca tersimpan dan penyegarannya

public class WeatherFreshnessTests
{
    private const string HotspotId = "HS-0142";

    private static (PredictionService svc, AppDbContext db, FakeHandler handler, IntegrationStatus status, Hotspot hotspot) Make(
        string? weatherKey = "KUNCI-CUACA-456", int maxAgeMinutes = 60)
    {
        var db = TestDb.Create();
        var options = Samples.Options(weatherKey: weatherKey);
        options.WeatherMaxAgeMinutes = maxAgeMinutes;
        var handler = new FakeHandler().Enqueue(HttpStatusCode.OK, Samples.WeatherJson, "application/json");
        var status = new IntegrationStatus();
        var weather = new WeatherClient(new HttpClient(handler), options, status);
        var svc = new PredictionService(db, weather, new FwiPredictionModel(), new ActivityLogService(db), options);
        var hotspot = db.Hotspots.First(h => h.Id == HotspotId);
        return (svc, db, handler, status, hotspot);
    }

    private static async Task AddWeatherAsync(AppDbContext db, TimeSpan age, string id = "WX-9001", double temp = 30)
    {
        db.WeatherData.Add(new WeatherData
        {
            Id = id, HotspotId = HotspotId, Latitude = 0, Longitude = 0, Timestamp = DateTime.Now - age,
            Temperature = temp, Humidity = 60, WindSpeed = 3, WindDirection = 90, Pressure = 1010, Condition = "tersimpan"
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FirstCall_FetchesAndStores_ThenFreshDataIsReused()
    {
        var (svc, db, handler, _, hotspot) = Make();

        var first = await svc.GetWeatherAsync(hotspot);
        var second = await svc.GetWeatherAsync(hotspot);

        Assert.Equal(1, handler.Calls);                       // yang kedua tidak memanggil OpenWeather lagi
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(29.7, first.Temperature);                // cuaca nyata, bukan simulasi
        Assert.Equal(1, await db.WeatherData.CountAsync(w => w.HotspotId == HotspotId));
    }

    [Fact]
    public async Task StaleData_IsRefetched_AsANewRow()
    {
        var (svc, db, handler, _, hotspot) = Make();
        await AddWeatherAsync(db, age: TimeSpan.FromHours(3));

        var w = await svc.GetWeatherAsync(hotspot);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(29.7, w.Temperature);
        Assert.NotEqual("WX-9001", w.Id);
        Assert.Equal(2, await db.WeatherData.CountAsync(x => x.HotspotId == HotspotId));
    }

    [Fact]
    public async Task FreshData_IsNotRefetched()
    {
        var (svc, db, handler, _, hotspot) = Make();
        await AddWeatherAsync(db, age: TimeSpan.FromMinutes(5));

        var w = await svc.GetWeatherAsync(hotspot);

        Assert.Equal(0, handler.Calls);
        Assert.Equal("WX-9001", w.Id);
    }

    [Fact]
    public async Task WhenRefetchFails_TheStoredWeatherIsKept_NotReplacedBySimulation()
    {
        var (svc, db, handler, status, hotspot) = Make();
        await AddWeatherAsync(db, age: TimeSpan.FromHours(3), temp: 31.5);
        handler.Reset().Enqueue(HttpStatusCode.ServiceUnavailable);

        var w = await svc.GetWeatherAsync(hotspot);

        Assert.Equal("WX-9001", w.Id);
        Assert.Equal(31.5, w.Temperature);
        Assert.Equal(1, await db.WeatherData.CountAsync(x => x.HotspotId == HotspotId));   // tidak ada baris simulasi baru
        Assert.NotNull(status.WeatherLastError);
    }

    [Fact]
    public async Task WithoutKey_FirstCallStoresSimulation_AndLaterCallsAddNothing()
    {
        var (svc, db, handler, _, hotspot) = Make(weatherKey: "");
        if (new WeatherClient(new HttpClient(), Samples.Options(weatherKey: "")).IsConfigured)
            return;   // mesin ini punya OPENWEATHER_API_KEY di environment; tes ini tidak relevan di sana

        var first = await svc.GetWeatherAsync(hotspot);
        Assert.Equal("Simulasi", first.Condition);

        // jadikan usang lalu panggil lagi: tanpa key tidak ada yang bisa disegarkan, jadi tidak menumpuk baris yang sama
        var row = await db.WeatherData.FirstAsync(w => w.Id == first.Id);
        row.Timestamp = DateTime.Now.AddHours(-5);
        await db.SaveChangesAsync();

        var again = await svc.GetWeatherAsync(hotspot);
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(1, await db.WeatherData.CountAsync(w => w.HotspotId == HotspotId));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Predict_UsesRealWeather_WhenAvailable()
    {
        var (svc, db, handler, _, hotspot) = Make();

        var prediction = await svc.PredictAsync(hotspot);

        Assert.Equal(1, handler.Calls);
        var stored = await db.WeatherData.FirstAsync(w => w.Id == prediction.WeatherId);
        Assert.Equal(29.7, stored.Temperature);
    }
}

// ------------------------------------------------------------------------------------------------ endpoint status integrasi

public class IntegrationStatusApiTests : IClassFixture<IntegrationStatusApiTests.Factory>
{
    /// <summary>Hermetik: opsi integrasi dikosongkan supaya tes tidak memanggil layanan luar walau .env berisi key sungguhan.</summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:Provider", "InMemory");
            builder.UseSetting("Storage:InMemoryName", "integrasi-tes-" + Guid.NewGuid());
            builder.UseSetting("Jwt:Key", "kunci-rahasia-untuk-tes-minimal-32-karakter!");
            builder.UseSetting("Auth:RateLimitEnabled", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IntegrationOptions>();
                services.AddSingleton(new IntegrationOptions());   // tanpa key
            });
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly Factory _factory;
    public IntegrationStatusApiTests(Factory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, DemoData.Password));
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    [Fact]
    public async Task WithoutToken_Is401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/integrations/status")).StatusCode);

    [Fact]
    public async Task RegularUser_Is403() =>
        Assert.Equal(HttpStatusCode.Forbidden, (await (await LoginAsync("andi.pratama@example.com")).GetAsync("/api/integrations/status")).StatusCode);

    [Fact]
    public async Task Admin_SeesBothProviders_AndFirmsWithoutKeyIsMissingKey()
    {
        var client = await LoginAsync("admin@ignis.example");
        var list = (await client.GetFromJsonAsync<List<ProviderStatusDto>>("/api/integrations/status", Json))!;

        Assert.Equal(new[] { "NASA FIRMS", "OpenWeather" }, list.Select(p => p.Name).ToArray());

        var firms = list[0];
        Assert.False(firms.KeyConfigured);
        Assert.Equal(ConnectionState.MissingKey, firms.State);
        Assert.Contains("FirmsKey", firms.Message);

        var weather = list[1];   // tergantung env OPENWEATHER_API_KEY di mesin: cukup pastikan jawabannya utuh
        Assert.False(string.IsNullOrWhiteSpace(weather.Message));
        Assert.Equal(weather.KeyConfigured, weather.State != ConnectionState.MissingKey);
    }

    [Fact]
    public async Task Sync_WithoutKeys_ReportsMissingKey_AndDoesNotFail()
    {
        var client = await LoginAsync("admin@ignis.example");
        var resp = await client.PostAsync("/api/sync", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = (await resp.Content.ReadFromJsonAsync<SyncResult>(Json))!;
        Assert.Equal(ConnectionState.MissingKey, result.Firms);
        Assert.Equal(0, result.NewHotspots);
    }
}
