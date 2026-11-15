namespace Ignis.Core.Enums;

public enum Role { Registered, Admin }

public enum HotspotSource { Satellite, Manual }

public enum HotspotStatus { Active, Monitoring, Resolved }

public enum RiskLevel { Low, Medium, High, Critical }

/// <summary>Status koneksi ke layanan eksternal (NASA FIRMS / OpenWeather).</summary>
public enum ConnectionState { Unknown, Syncing, Connected, Failed, MissingKey }
