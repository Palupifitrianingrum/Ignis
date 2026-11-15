# Fitur Tugas OOP: Manajemen Hotspot

Fitur: **admin menambah, mengubah, dan menghapus hotspot manual**, sedangkan hotspot satelit (NASA FIRMS) hanya dibaca.
Seluruh data disimpan di PostgreSQL. Alurnya: `WPF (form admin)` → HTTP → `Ignis.Api` → `HotspotService` → entity `Hotspot` → `EfHotspotRepository` → PostgreSQL.

Method utama: `HotspotService.AddManualAsync` ([Ignis.Core/Services/HotspotService.cs](../Ignis.Core/Services/HotspotService.cs)).

## Di mana tiga konsep itu diterapkan

| Konsep | Penerapan | Berkas |
|---|---|---|
| **Inheritance** | `Hotspot` (abstrak) diturunkan menjadi `SatelliteHotspot` dan `ManualHotspot`. `User` (abstrak) diturunkan menjadi `Admin` dan `RegisteredUser`. Sama seperti ERD (tabel anak berbagi PK/FK dengan tabel induk). | [Hotspot.cs](../Ignis.Core/Entities/Hotspot.cs), [User.cs](../Ignis.Core/Entities/User.cs) |
| **Encapsulation** | Semua properti `Hotspot`/`User` bersetter privat. Nilai berubah hanya lewat konstruktor dan method (`UpdateReport`, `ChangeStatus`, `AssignId`, `Activate`/`Deactivate`) yang memvalidasi invarian (lat/lon, confidence 0–100, pelapor wajib, Id hanya sekali). `UpdateReport` bersifat atomik: input tidak valid tidak mengubah apa pun. | [Hotspot.cs](../Ignis.Core/Entities/Hotspot.cs), [DomainException.cs](../Ignis.Core/Common/DomainException.cs) |
| **Polymorphism** | `Hotspot.IsEditable` dan `Hotspot.GetDetails()` abstrak, tiap subclass punya perilaku sendiri. `HotspotService.DeleteAsync` cukup memanggil `existing.IsEditable` tanpa `if` per jenis. `User.CanManageHotspots` juga polimorfik. | [HotspotService.cs](../Ignis.Core/Services/HotspotService.cs) |

Bukti perilakunya ada di unit test: [HotspotOopTests](../Ignis.Core.Tests/CoreLogicTests.cs) (`dotnet test Ignis.Core.Tests`).

## Alur "tambah hotspot manual"

```mermaid
sequenceDiagram
    actor Admin
    participant FE as HotspotFormWindow (WPF)
    participant WS as AdminWorkspace
    participant API as Ignis.Api
    participant SVC as HotspotService
    participant ENT as ManualHotspot (entity)
    participant REPO as EfHotspotRepository
    participant DB as PostgreSQL

    Admin->>FE: isi form, klik Simpan
    FE->>FE: validasi ringan (pesan cepat)
    FE->>WS: AddManualAsync(ManualHotspotRequest)
    WS->>API: POST /api/hotspots/manual
    API->>SVC: AddManualAsync(request)
    SVC->>ENT: new ManualHotspot(...) — memvalidasi invarian
    alt input melanggar aturan domain
        ENT-->>API: DomainException
        API-->>FE: HTTP 400 {error}
        FE-->>Admin: pesan error
    else valid
        SVC->>REPO: AddAsync(hotspot)
        REPO->>DB: INSERT hotspots + manual_hotspots
        DB-->>REPO: ok
        REPO-->>SVC: hotspot (Id HS-xxxx)
        SVC-->>API: ManualHotspot
        API-->>FE: 201 HotspotDto (Details dari GetDetails())
        FE-->>Admin: daftar hotspot diperbarui
    end
```

## Diagram kelas fitur

```mermaid
classDiagram
    direction TB

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
        +IsEditable bool
        +GetDetails() string
    }
    class ManualHotspot {
        +string ReportedBy
        +string Note
        +DateTime IncidentTime
        +IsEditable bool
        +GetDetails() string
        +UpdateReport(...) void
    }
    class HotspotService {
        +AddManualAsync(request) ManualHotspot
        +UpdateManualAsync(id, request) ManualHotspot
        +DeleteAsync(id) bool
        +ImportSatelliteAsync(hotspots) int
    }
    class IHotspotRepository {
        <<interface>>
        +GetAllAsync()
        +AddAsync(hotspot)
        +UpdateAsync(hotspot)
        +DeleteAsync(id)
    }
    class EfHotspotRepository
    class InMemoryHotspotRepository
    class DomainException

    Hotspot <|-- SatelliteHotspot
    Hotspot <|-- ManualHotspot
    HotspotService --> IHotspotRepository
    IHotspotRepository <|.. EfHotspotRepository
    IHotspotRepository <|.. InMemoryHotspotRepository
    HotspotService ..> Hotspot : memperlakukan semua sebagai Hotspot
    Hotspot ..> DomainException : melempar bila tidak valid
```

## Mencoba

Lihat bagian "Menjalankan" di [README](../README.md). Singkatnya: nyalakan PostgreSQL, jalankan `Ignis.Api` dengan
`Storage__Provider=Postgres`, jalankan `Ignis.Frontend`, login `admin` / `admin`, buka **Kelola Hotspot** lalu **Tambah Hotspot**.
Hasilnya bisa dicek langsung di database: `SELECT * FROM hotspots h JOIN manual_hotspots m USING (hotspot_id);`
