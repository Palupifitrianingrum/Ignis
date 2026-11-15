# Database Ignis (PostgreSQL)

Skema mengikuti ERD proyek. Skrip lengkap: [database/schema.sql](../database/schema.sql) (dihasilkan dari
[AppDbContext](../Ignis.Infrastructure/Persistence/AppDbContext.cs)). Tabel dibuat otomatis oleh `Ignis.Api`
saat start bila `Storage:Provider=Postgres`.

## ERD (hasil implementasi)

```mermaid
erDiagram
    users ||--o| admins : "is a"
    users ||--o| registered_users : "is a"
    registered_users ||--o{ monitoring_areas : creates
    registered_users ||--o{ notifications : receives
    hotspots ||--o| satellite_hotspots : "is a"
    hotspots ||--o| manual_hotspots : "is a"
    hotspots ||--o{ notifications : generates
    hotspots ||--o{ weather_data : "associated with"
    hotspots ||--o{ predictions : "based on"
    weather_data |o--o{ predictions : "used by"

    users {
        varchar user_id PK
        varchar name
        varchar email UK
        text password_hash
        varchar role "Admin | Registered"
        timestamp created_at
        boolean is_active "tambahan"
    }
    admins {
        varchar user_id PK,FK
    }
    registered_users {
        varchar user_id PK,FK
    }
    monitoring_areas {
        varchar area_id PK
        varchar user_id FK
        varchar name
        double min_latitude
        double max_latitude
        double min_longitude
        double max_longitude
        double radius
    }
    hotspots {
        varchar hotspot_id PK
        double latitude
        double longitude
        timestamp detected_at
        double confidence
        double brightness
        varchar source "Satellite | Manual"
        varchar status "Active | Monitoring | Resolved"
    }
    satellite_hotspots {
        varchar hotspot_id PK,FK
        varchar satellite_name
        varchar instrument
        timestamp scan_time
    }
    manual_hotspots {
        varchar hotspot_id PK,FK
        varchar reported_by
        timestamp incident_time
        varchar note "tambahan"
    }
    notifications {
        varchar notification_id PK
        varchar user_id FK
        varchar hotspot_id FK
        varchar title
        text message
        timestamp created_at
        boolean is_read
    }
    weather_data {
        varchar weather_id PK
        varchar hotspot_id FK
        double latitude
        double longitude
        timestamp timestamp
        double temperature
        double humidity
        double wind_speed
        double wind_direction
        double pressure
        double precipitation
        varchar weather_condition
    }
    predictions {
        varchar prediction_id PK
        varchar hotspot_id FK
        varchar weather_id FK "boleh null"
        timestamp created_at
        double spread_direction
        double spread_speed
        double estimated_area
        double estimated_time
        varchar risk_level "Low | Medium | High | Critical"
    }
```

## Selisih dengan ERD asli

| Hal | Keputusan | Alasan |
|---|---|---|
| Tabel `USER` | dinamai `users` | `user` adalah kata cadangan di PostgreSQL |
| Nama kolom `camelCase` | `snake_case` (`userId` → `user_id`) | konvensi PostgreSQL |
| `users.is_active` | **kolom tambahan** | dipakai fitur aktif/nonaktif pengguna di halaman admin |
| `manual_hotspots.note` | **kolom tambahan** | catatan pada form hotspot manual |
| tabel `activity_log` | **tabel tambahan** | log aktivitas di halaman pengguna |
| relasi `MONITORING_AREA` ↔ `HOTSPOT` ("monitors / refers to") | **tidak punya FK** | di ERD tidak ada kolom penghubung; dihitung secara spasial lewat `MonitoringArea.Contains(lat, lon)` |
| `predictions.weather_id` | boleh `NULL` | data prediksi contoh tidak punya baris cuaca; bila cuaca dihapus nilainya jadi `NULL` |
| Hapus hotspot | menghapus anak-anaknya (cuaca, prediksi, notifikasi) lewat `ON DELETE CASCADE` | FK `hotspot_id` pada ERD wajib terisi |
| Pewarisan tabel | `hotspot_id`/`user_id` di tabel anak = PK sekaligus FK | sesuai ERD (`PK,FK`) |

Usul: tambahkan `is_active`, `note`, dan `activity_log` ke ERD resmi agar dokumen dan database sama.

## Aturan data yang dijaga database

`CHECK` pada `hotspots` (lat −90..90, lon −180..180, confidence 0..100, `source`, `status`), `users.role`,
`predictions.risk_level`, dan `monitoring_areas` (min ≤ max, radius ≥ 0); email pengguna unik; hotspot satelit unik
per `(detected_at, latitude, longitude)` (mencegah impor FIRMS ganda).

## Menyalakan PostgreSQL

Lihat bagian "Menjalankan" di [README](../README.md).
