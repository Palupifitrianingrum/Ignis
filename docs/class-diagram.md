# Class Diagram Ignis (struktur baru)

Dua diagram: **domain** (entity dan relasinya) dan **layanan** (service, interface, implementasi per proyek).
Salin blok `mermaid` ke https://mermaid.live atau pratinjau Markdown yang mendukung Mermaid.
Diagram basis data ada di [database.md](database.md); fitur untuk tugas OOP di [oop-feature.md](oop-feature.md).

## 1. Domain (`Ignis.Core`)

```mermaid
classDiagram
    direction TB

    class Role {
        <<enumeration>>
        Registered
        Admin
    }
    class HotspotSource {
        <<enumeration>>
        Satellite
        Manual
    }
    class HotspotStatus {
        <<enumeration>>
        Active
        Monitoring
        Resolved
    }
    class RiskLevel {
        <<enumeration>>
        Low
        Medium
        High
        Critical
    }

    class Coordinate {
        <<value object>>
        +double Latitude
        +double Longitude
    }
    class Vector {
        <<value object>>
        +double Magnitude
        +double DirectionDegrees
    }
    class Region {
        <<value object>>
        +string Name
        +Contains(lat, lon) bool
    }

    class User {
        <<abstract>>
        +string Id
        +string Name
        +string Email
        +string PasswordHash
        +Role Role
        +DateTime CreatedAt
        +bool IsActive
        +Activate() void
        +Deactivate() void
        +CanManageHotspots bool*
    }
    class Admin {
        +CanManageHotspots bool
    }
    class RegisteredUser {
        +CanManageHotspots bool
    }

    class Hotspot {
        <<abstract>>
        +string Id
        +HotspotSource Source
        +double Latitude
        +double Longitude
        +DateTime DetectedAt
        +double Confidence
        +double Brightness
        +HotspotStatus Status
        +AssignId(id) void
        +ChangeStatus(status) void
        +GetLocation() Coordinate
        +IsEditable bool*
        +GetDetails() string*
    }
    class SatelliteHotspot {
        +string SatelliteName
        +string Instrument
        +DateTime ScanTime
    }
    class ManualHotspot {
        +string ReportedBy
        +string Note
        +DateTime IncidentTime
        +UpdateReport(...) void
    }

    class WeatherData {
        +string Id
        +string HotspotId
        +double Latitude
        +double Longitude
        +DateTime Timestamp
        +double Temperature
        +double Humidity
        +double WindSpeed
        +double WindDirection
        +double Pressure
        +double Precipitation
        +string Condition
        +GetWindVector() Vector
    }

    class Prediction {
        +string Id
        +string HotspotId
        +string WeatherId
        +double Direction
        +double SpeedKmH
        +double AreaHa
        +double Hours
        +RiskLevel Risk
        +DateTime CreatedAt
    }

    class MonitoringArea {
        +string Id
        +string UserId
        +string Name
        +double MinLatitude
        +double MaxLatitude
        +double MinLongitude
        +double MaxLongitude
        +double Radius
        +Contains(lat, lon) bool
    }

    class Notification {
        +string Id
        +string UserId
        +string HotspotId
        +string Title
        +string Message
        +DateTime CreatedAt
        +bool IsRead
        +MarkAsRead() void
    }

    User <|-- Admin
    User <|-- RegisteredUser
    Hotspot <|-- SatelliteHotspot
    Hotspot <|-- ManualHotspot

    RegisteredUser "1" --> "0..*" MonitoringArea : membuat
    RegisteredUser "1" --> "0..*" Notification : menerima
    Hotspot "1" --> "0..*" Notification : menghasilkan
    Hotspot "1" --> "0..*" WeatherData : terkait cuaca
    Hotspot "1" --> "0..*" Prediction : dasar prediksi
    WeatherData "0..1" --> "0..*" Prediction : dipakai oleh
    Prediction ..> RiskLevel
    Hotspot ..> HotspotStatus
    Hotspot ..> HotspotSource
    Hotspot ..> Coordinate
    WeatherData ..> Vector
    User ..> Role
```

Catatan: `User` dan `Hotspot` abstrak dengan setter privat (enkapsulasi); `CanManageHotspots`, `IsEditable`, dan `GetDetails()`
dioverride tiap subclass (polimorfisme). `login()/logout()` ada di `IAuthService`; operasi tambah/ubah/hapus hotspot ada di `HotspotService`.

## 2. Layanan, interface, dan implementasi

```mermaid
classDiagram
    direction LR

    namespace Core_Abstractions {
        class IHotspotRepository {
            <<interface>>
        }
        class IWeatherRepository {
            <<interface>>
        }
        class IPredictionRepository {
            <<interface>>
        }
        class IUserRepository {
            <<interface>>
        }
        class IActivityLog {
            <<interface>>
        }
        class IFirmsClient {
            <<interface>>
        }
        class IWeatherProvider {
            <<interface>>
        }
        class ISettingsProvider {
            <<interface>>
        }
        class IPredictionModel {
            <<interface>>
        }
        class IFireSpreadSimulator {
            <<interface>>
        }
        class IAuthService {
            <<interface>>
        }
        class INotifier {
            <<interface>>
        }
    }

    namespace Core_Services {
        class HotspotService {
            +AddManualAsync(request)
            +UpdateManualAsync(id, request)
            +DeleteAsync(id)
            +ImportSatelliteAsync(hotspots)
        }
        class PredictionService {
            +PredictAsync(hotspot)
            +GetOrPredictAsync(hotspot)
            +GetWeatherAsync(hotspot)
        }
        class UserService {
            +ToggleActiveAsync(id)
        }
        class HotspotSyncService {
            +RunAsync(progress)
        }
        class FirmsCsvParser
        class FwiPredictionModel
        class FallbackWeatherProvider
        class SimulatedWeatherProvider
        class CellularAutomataSimulator
        class InMemoryRepositories
    }

    namespace Integrations {
        class FirmsClient
        class OpenWeatherClient
    }

    namespace Infrastructure {
        class AppDbContext
        class EfRepositories
        class DatabaseInitializer
        class AuthService
        class NotificationService
    }

    namespace Frontend_WPF {
        class AdminWorkspace
        class IAdminBackend {
            <<interface>>
        }
        class ApiAdminBackend
        class LocalAdminBackend
        class HotspotRow
        class AdminViews
    }

    namespace Api {
        class IgnisApi
        class SyncBackgroundService
    }

    %% service memakai abstraksi
    HotspotService --> IHotspotRepository
    HotspotService --> IActivityLog
    PredictionService --> IPredictionRepository
    PredictionService --> IWeatherRepository
    PredictionService --> IWeatherProvider
    PredictionService --> IPredictionModel
    UserService --> IUserRepository
    HotspotSyncService --> IFirmsClient
    HotspotSyncService --> IWeatherProvider
    HotspotSyncService --> HotspotService
    HotspotSyncService --> ISettingsProvider
    HotspotSyncService ..> FirmsCsvParser

    %% implementasi
    IPredictionModel <|.. FwiPredictionModel
    IWeatherProvider <|.. FallbackWeatherProvider
    IWeatherProvider <|.. SimulatedWeatherProvider
    IWeatherProvider <|.. OpenWeatherClient
    FallbackWeatherProvider --> OpenWeatherClient : utama
    FallbackWeatherProvider --> SimulatedWeatherProvider : cadangan
    IFirmsClient <|.. FirmsClient
    IFireSpreadSimulator <|.. CellularAutomataSimulator
    IAuthService <|.. AuthService
    INotifier <|.. NotificationService
    IHotspotRepository <|.. InMemoryRepositories
    IHotspotRepository <|.. EfRepositories
    IWeatherRepository <|.. EfRepositories
    IPredictionRepository <|.. EfRepositories
    IUserRepository <|.. EfRepositories
    IActivityLog <|.. EfRepositories
    EfRepositories --> AppDbContext
    DatabaseInitializer --> AppDbContext

    %% frontend: view -> workspace -> backend (API atau demo)
    AdminViews --> AdminWorkspace
    AdminWorkspace --> IAdminBackend
    AdminWorkspace ..> HotspotRow
    IAdminBackend <|.. ApiAdminBackend
    IAdminBackend <|.. LocalAdminBackend
    ApiAdminBackend ..> IgnisApi : HTTP
    LocalAdminBackend --> HotspotService
    LocalAdminBackend --> PredictionService
    LocalAdminBackend --> UserService

    %% API memanggil service
    IgnisApi --> HotspotService
    IgnisApi --> PredictionService
    IgnisApi --> UserService
    IgnisApi --> HotspotSyncService
    SyncBackgroundService --> HotspotSyncService

    note for CellularAutomataSimulator "TODO: belum diimplementasikan (stub)"
    note for AuthService "TODO: belum diimplementasikan (stub)"
    note for NotificationService "TODO: belum diimplementasikan (stub)"
    note for EfRepositories "PostgreSQL: Hotspot, Weather, Prediction, User, ActivityLog"
    note for InMemoryRepositories "Mode demo dan pengujian"
```

Arah dependensi proyek: `Frontend → Core ← Infrastructure`, `Integrations → Core`, `Api → Core + Infrastructure + Integrations`.
