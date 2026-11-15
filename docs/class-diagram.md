# Class Diagram Ignis

Tiga diagram: **domain** (entity dan relasinya), **server** (service, endpoint, database, integrasi di `Ignis.Api`), dan **desktop** (`Ignis.Frontend`).
Salin blok `mermaid` ke https://mermaid.live atau pratinjau Markdown yang mendukung Mermaid.
Diagram basis data ada di [database.md](database.md); fitur untuk tugas OOP di [oop-feature.md](oop-feature.md); peta lokasi file di [README](../README.md#peta-kode-class-itu-ada-di-mana).

## 1. Domain (`Ignis.Api/Domain`, enum di `Ignis.Shared`)

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
dioverride tiap subclass (polimorfisme). Login/registrasi ada di `AuthService`; operasi tambah/ubah/hapus hotspot ada di `HotspotService`.

## 2. Server (`Ignis.Api`)

Service memakai `AppDbContext` langsung (EF Core sudah berperan sebagai repository), tanpa lapisan interface tambahan.

```mermaid
classDiagram
    direction LR

    namespace Endpoints {
        class AuthEndpoints
        class HotspotEndpoints
        class AdminEndpoints
        class Policies
    }

    namespace Services {
        class AuthService {
            +RegisterAsync(request)
            +LoginAsync(request)
            +LoginWithGoogleAsync(request)
        }
        class TokenService {
            +CreateToken(user)
        }
        class Passwords {
            <<static>>
            +Hash(password)
            +Verify(hash, password)
        }
        class HotspotService {
            +GetAllAsync(source, status)
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
        class SyncService {
            +RunAsync()
        }
        class SyncBackgroundService
        class ActivityLogService
        class FwiPredictionModel
        class FireSpreadSimulator
    }

    namespace Integrations {
        class FirmsClient
        class FirmsCsvParser
        class WeatherClient
        class GoogleOAuthClient
    }

    namespace Data {
        class AppDbContext {
            +NextIdAsync(kind)
        }
        class DatabaseInitializer
        class DemoData
    }

    %% route -> service
    AuthEndpoints --> AuthService
    HotspotEndpoints --> HotspotService
    HotspotEndpoints --> PredictionService
    AdminEndpoints --> UserService
    AdminEndpoints --> SyncService
    AdminEndpoints --> ActivityLogService
    HotspotEndpoints ..> Policies
    AdminEndpoints ..> Policies

    %% service -> pendukung
    AuthService --> AppDbContext
    AuthService --> TokenService
    AuthService --> GoogleOAuthClient
    AuthService ..> Passwords
    HotspotService --> AppDbContext
    HotspotService --> ActivityLogService
    PredictionService --> AppDbContext
    PredictionService --> WeatherClient
    PredictionService --> FwiPredictionModel
    UserService --> AppDbContext
    ActivityLogService --> AppDbContext
    SyncService --> FirmsClient
    SyncService --> WeatherClient
    SyncService --> HotspotService
    SyncService ..> FirmsCsvParser
    SyncBackgroundService --> SyncService
    DatabaseInitializer --> AppDbContext
    DatabaseInitializer ..> DemoData
    DemoData ..> Passwords

    note for FireSpreadSimulator "TODO: belum diimplementasikan (stub)"
    note for AppDbContext "PostgreSQL atau EF InMemory. Id dibuat dari sequence database."
    note for WeatherClient "OpenWeather bila ada key, selain itu data simulasi"
```

## 3. Desktop (`Ignis.Frontend`)

```mermaid
classDiagram
    direction LR

    class IgnisApiClient {
        +CurrentUser UserDto
        +IsLoggedIn bool
        +IsAdmin bool
        +LoginAsync(email, password)
        +RegisterAsync(name, email, password)
        +LoginWithGoogleAsync(code, verifier, redirectUri)
        +GetHotspotsAsync()
        +SyncAsync()
    }
    class GoogleSignIn {
        <<static>>
        +RunAsync(clientId)
    }
    class AdminWorkspace
    class AppSettings
    class LoginFormViewModel
    class RegisterViewModel
    class MainWindowViewModel
    class AdminViews
    class IgnisApi {
        <<Ignis.Api>>
    }

    LoginFormViewModel --> IgnisApiClient
    LoginFormViewModel ..> GoogleSignIn
    RegisterViewModel --> IgnisApiClient
    MainWindowViewModel --> IgnisApiClient : cek login dan role
    AdminViews --> AdminWorkspace
    AdminWorkspace --> IgnisApiClient
    AdminWorkspace --> AppSettings
    IgnisApiClient ..> IgnisApi : HTTP + JWT
```

Arah dependensi proyek: `Frontend → Shared ← Api`. Frontend hanya bicara ke server lewat HTTP.
