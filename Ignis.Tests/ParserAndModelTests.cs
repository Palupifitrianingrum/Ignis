using Ignis.Api.Domain;
using Ignis.Api.Integrations;
using Ignis.Api.Services;
using Ignis.Shared;
using Xunit;

namespace Ignis.Tests;

// ====================== Parser FIRMS, model prediksi, cuaca ======================

public class FirmsCsvParserTests
{
    private const string Header = "latitude,longitude,bright_ti4,scan,track,acq_date,acq_time,satellite,instrument,confidence,version,bright_ti5,frp,daynight";

    [Fact]
    public void Parse_ReadsRow()
    {
        var csv = Header + "\n-0.39,102.06,342.5,0.4,0.4,2026-10-24,0915,N,VIIRS,87,2.0NRT,290,5,D\n";
        var h = Assert.Single(FirmsCsvParser.Parse(csv));

        Assert.Equal(-0.39, h.Latitude, 5);
        Assert.Equal(new DateTime(2026, 10, 24, 9, 15, 0), h.ScanTime);
        Assert.Equal(new DateTime(2026, 10, 24, 9, 15, 0), h.DetectedAt);
        Assert.Equal(342.5, h.Brightness, 3);
        Assert.Equal("N", h.SatelliteName);
    }

    [Fact]
    public void Parse_PadsShortTime_UsesDefaultConfidence_SkipsBadRows()
    {
        var csv = Header + "\n1,2,300,0,0,2026-10-24,5,N,VIIRS,x,v,0,0,D\nbad,row\n";
        var h = Assert.Single(FirmsCsvParser.Parse(csv));

        Assert.Equal(new DateTime(2026, 10, 24, 0, 5, 0), h.ScanTime);
        Assert.Equal(70, h.Confidence);
    }

    [Fact]
    public void Parse_SkipsRowsViolatingDomainRules()
    {
        var csv = Header + "\n95,2,300,0,0,2026-10-24,0900,N,VIIRS,80,v,0,0,D\n";   // latitude 95 tidak valid
        Assert.Empty(FirmsCsvParser.Parse(csv));
    }

    [Fact]
    public void Parse_MissingRequiredColumns_ReturnsEmpty() =>
        Assert.Empty(FirmsCsvParser.Parse("foo,bar\n1,2\n"));
}

public class FwiPredictionModelTests
{
    private static Prediction Run(double temp, double hum, double wind, double dir, double rain)
    {
        var h = new ManualHotspot(0, 100, 50, DateTime.Now, "x");
        h.AssignId("HS-0001");
        var w = new WeatherData { Id = "WX-0001", Temperature = temp, Humidity = hum, WindSpeed = wind, WindDirection = dir, Precipitation = rain };
        return new FwiPredictionModel().Predict(h, w);
    }

    [Fact]
    public void Direction_IsOppositeOfWind_AndLinksWeather()
    {
        var p = Run(30, 60, 5, 90, 0);
        Assert.Equal(270, p.Direction);
        Assert.Equal("WX-0001", p.WeatherId);
        Assert.Equal("HS-0001", p.HotspotId);
    }

    [Fact]
    public void CalmWetWeather_UsesMinimumSpeed()
    {
        var p = Run(25, 95, 0.5, 0, 4);
        Assert.Equal(0.05, p.SpeedKmH);
        Assert.Equal(12, p.Hours);
        Assert.Equal(22.6, p.AreaHa);
        Assert.Equal(RiskLevel.Medium, p.Risk);
    }

    [Fact]
    public void HotDryWindyWeather_IsCritical()
    {
        var p = Run(40, 30, 9, 0, 0);
        Assert.Equal(RiskLevel.Critical, p.Risk);
        Assert.Equal(3, p.Hours);
    }

    [Fact]
    public void Speed_IsClamped() => Assert.InRange(Run(60, 0, 50, 0, 0).SpeedKmH, 0.05, 3.5);
}

public class WeatherSimulationTests
{
    [Fact]
    public void SameCoordinate_GivesSameWeather()
    {
        var a = WeatherClient.Simulate(-2.9, 104.7);
        var b = WeatherClient.Simulate(-2.9, 104.7);
        Assert.Equal(a.Temperature, b.Temperature);
        Assert.Equal(a.WindDirection, b.WindDirection);
    }

    [Fact]
    public async Task WithoutApiKey_FallsBackToSimulation()
    {
        var client = new WeatherClient(new HttpClient(), new IntegrationOptions());
        if (client.IsConfigured) return;   // mesin yang punya env OPENWEATHER_API_KEY: tes ini tidak relevan

        var w = await client.GetWeatherAsync(-2.9, 104.7);
        Assert.Equal("Simulasi", w.Condition);
    }
}

public class FireSpreadSimulatorTests
{
    [Fact]
    public void Simulate_IsDeterministic_AndAreaDoesNotShrink()
    {
        var h = new ManualHotspot(0.32, 101.85, 85, DateTime.Now, "Petugas");
        h.AssignId("HS-0001");
        var w = new WeatherData { Id = "WX-0001", Temperature = 34, Humidity = 40, WindSpeed = 8, WindDirection = 270, Precipitation = 0 };

        var sim = new FireSpreadSimulator();
        var frames1 = sim.Simulate(h, w, 6).ToList();
        var frames2 = sim.Simulate(h, w, 6).ToList();

        Assert.Equal(7, frames1.Count);
        for (int i = 0; i < frames1.Count; i++)
        {
            Assert.Equal(frames1[i].AreaHa, frames2[i].AreaHa);
            Assert.Equal(frames1[i].BurningCells.Count, frames2[i].BurningCells.Count);
            if (i > 0) Assert.True(frames1[i].AreaHa >= frames1[i - 1].AreaHa);
        }
        Assert.True(frames1[^1].BurningCells.Count > 1);
    }
}
