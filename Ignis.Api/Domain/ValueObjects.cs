namespace Ignis.Api.Domain;

public readonly record struct Coordinate(double Latitude, double Longitude);

/// <summary>Vektor 2D sederhana (mis. angin). Arah dalam derajat (0-360).</summary>
public readonly record struct Vector(double Magnitude, double DirectionDegrees);
