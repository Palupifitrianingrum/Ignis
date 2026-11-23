using System.Collections.Generic;

namespace Ignis.Frontend.Models;

/// <summary>Satu sel api pada peta. <paramref name="Age"/> = jam sejak sel itu menyala (0 = baru menyala pada jam ini).</summary>
public sealed record SimulationCellView(double Latitude, double Longitude, int Age);

/// <summary>Satu sel asap pada peta (sudah dalam lintang/bujur). Level 1 ringan, 2 sedang, 3 pekat.</summary>
public sealed record SmokeCellView(double Latitude, double Longitude, int Level);

/// <summary>Keadaan simulasi pada satu jam, siap digambar di peta (sel berwarna menurut umur, titik asal, arah rambatan).</summary>
public sealed record SimulationMapState(
    string HotspotId,
    double OriginLatitude,
    double OriginLongitude,
    double CellSizeDegrees,
    double SpreadDirection,
    int CurrentHour,
    int MaxHour,
    IReadOnlyList<SimulationCellView> Cells,
    IReadOnlyList<SmokeCellView> SmokeCells,
    double SmokeCellSizeDegrees);
