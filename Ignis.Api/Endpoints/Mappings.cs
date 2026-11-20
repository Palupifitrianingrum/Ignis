using Ignis.Api.Domain;
using Ignis.Api.Services;
using Ignis.Shared;

namespace Ignis.Api.Endpoints;

/// <summary>Pemetaan entity domain ke DTO.</summary>
public static class Mappings
{
    public static HotspotDto ToDto(this Hotspot h)
    {
        var sat = h as SatelliteHotspot;
        var man = h as ManualHotspot;
        return new HotspotDto(
            h.Id, h.Source, h.Latitude, h.Longitude, h.DetectedAt, h.Confidence, h.Brightness, h.Status,
            h.GetDetails(), h.IsEditable,
            sat?.SatelliteName, sat?.Instrument, sat?.ScanTime,
            man?.ReportedBy, man?.Note, man?.IncidentTime);
    }

    public static WeatherDto ToDto(this WeatherData w) => new(
        w.Id, w.HotspotId, w.Latitude, w.Longitude, w.Timestamp, w.Temperature, w.Humidity,
        w.WindSpeed, w.WindDirection, w.Pressure, w.Precipitation, w.Condition);

    public static PredictionDto ToDto(this Prediction p) => new(
        p.Id, p.HotspotId, p.WeatherId, p.Direction, p.SpeedKmH, p.AreaHa, p.Hours, p.Risk, p.CreatedAt);

    public static SimulationFrameDto ToDto(this SimulationFrame f) => new(
        f.HourOffset,
        f.BurningCells.Select(c => new CoordinateDto(c.Latitude, c.Longitude)).ToList(),
        f.AreaHa,
        f.NewCells);

    public static SimulationDto ToDto(this SimulationResult r) => new(
        r.HotspotId, r.OriginLatitude, r.OriginLongitude, r.Hours,
        r.SpreadDirection, r.WindFromDirection, r.WindSpeed,
        r.Temperature, r.Humidity, r.Precipitation, r.Condition,
        r.CellSizeDegrees, r.CellAreaHa, r.MaxCells, r.Truncated,
        r.Frames.Select(f => f.ToDto()).ToList());

    public static UserDto ToDto(this User u) => new(u.Id, u.Name, u.Email, u.Role, u.IsActive, u.CreatedAt);
}
