using System.Collections.Generic;

namespace Ignis.Frontend.Models;

/// <summary>Satu sel api pada peta. <paramref name="Age"/> = jam sejak sel itu menyala (0 = baru menyala pada jam ini).</summary>
public sealed record SimulationCellView(double Latitude, double Longitude, int Age);

/// <summary>Keadaan simulasi pada satu jam, siap digambar di peta (sel berwarna menurut umur, titik asal, arah rambatan).</summary>
public sealed record SimulationMapState(
    string HotspotId,
    double OriginLatitude,
    double OriginLongitude,
    double CellSizeDegrees,
    double SpreadDirection,
    int CurrentHour,
    int MaxHour,
    IReadOnlyList<SimulationCellView> Cells);
