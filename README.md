IGNIS</br>
Aplikasi desktop C# yang memetakan hotspot kebakaran secara real-time, menganalisis kondisi lingkungan, kemudian memperkirakan arah dan potensi penyebaran api dalam beberapa jam ke depan</br></br>
IGNIS SQUAD</br>
Ketua Kelompok: Natanael Sebastian Simanjuntak-24/542676/TK/60273</br>
Anggota 1: Muhammad Nafal Zakin Rustanto-24/535255/TK/59364</br>
Anggota 2: Wahyu Fajrin Adi Putra 24/548043/TK/59664</br>
Anggota 3: Palupi Fitria Ningrum-24/540125/TK/59904</br>
Anggota 4: Natanael Sebastian Simanjuntak-24/542676/TK/60273</br>

## Struktur solution

| Proyek | Peran |
|---|---|
| `Ignis.Core` | Domain + logika murni: entity, enum, interface, service (prediksi, hotspot, pengguna, sinkronisasi), parser FIRMS, implementasi in-memory (mode demo). Tanpa WPF/EF/HTTP. |
| `Ignis.Integrations` | Klien HTTP eksternal: NASA FIRMS dan OpenWeather. |
| `Ignis.Infrastructure` | Persistensi PostgreSQL (EF Core), DI penyimpanan, stub auth/notifikasi. |
| `Ignis.Api` | ASP.NET Core minimal API (hotspot, prediksi, pengguna, sinkronisasi). |
| `Ignis.Frontend` | Aplikasi WPF (View + ViewModel). Data lewat `Ignis.Api` (mode `Api`) atau data demo di memori (mode `Demo`). |
| `Ignis.Core.Tests` | Unit test logika Core. |

Arah dependensi: `Frontend → Core ← Infrastructure`, `Integrations → Core`, `Api → Core, Infrastructure, Integrations`.
Diagram kelas: [docs/class-diagram.md](docs/class-diagram.md).

## Menjalankan

```bash
dotnet build Ignis.slnx
dotnet test Ignis.Core.Tests
```

**A. Mode demo (tanpa database, data di memori)**

```bash
dotnet run --project Ignis.Api                           # API di http://localhost:5000
IGNIS_BACKEND=Demo dotnet run --project Ignis.Frontend   # desktop tanpa API (login: admin / admin)
```

**B. Dengan PostgreSQL (untuk tugas)**

1. Nyalakan PostgreSQL dan buat database `ignis` (pilih salah satu):
   - Docker dari terminal: salin `.env.example` ke `.env`, isi `IGNIS_DB_PASSWORD`, lalu `docker compose up -d`.
   - PostgreSQL yang sudah terpasang di laptop: buat database `ignis` beserta user-nya. Skema dibuat otomatis oleh API; skrip setara ada di `database/schema.sql`.
2. Di `.env` (root repo) isi `Storage__Provider=Postgres`, `ConnectionStrings__Ignis=...`, dan opsional `Storage__SeedDemoData=true` (mengisi data contoh bila tabel masih kosong).
3. `dotnet run --project Ignis.Api` (tabel dibuat otomatis), lalu cek `http://localhost:5000/health`.
4. `dotnet run --project Ignis.Frontend`, login `admin` / `admin`. Mode default adalah `Api` (alamat `http://localhost:5000/`, bisa diubah di `%AppData%\IgnisAdmin\settings.json`).
5. Buka **Kelola Hotspot**, klik **Tambah Hotspot**, lalu cek di database: `SELECT * FROM hotspots h JOIN manual_hotspots m USING (hotspot_id);`

Dokumentasi: [skema dan ERD](docs/database.md), [fitur untuk tugas OOP](docs/oop-feature.md), [diagram kelas](docs/class-diagram.md).

## Status backend (yang belum selesai)

Tanda `TODO(backend)` di kode menandai hal berikut:
- Autentikasi (JWT, register/login, Google OAuth) — `Ignis.Infrastructure/Services/AuthService.cs`. **Semua endpoint API masih terbuka tanpa auth.**
- Notifikasi dan area pantauan — `NotificationService.cs`, belum ada endpoint.
- Simulasi penyebaran api — `Ignis.Core/Simulation/CellularAutomataSimulator.cs` (stub).
- EF Core migration (skema dibuat dengan `EnsureCreated`; untuk produksi ganti dengan migration).
- Generator Id yang aman multi-user (sequence) dan pemindahan konfigurasi FIRMS/weather dari desktop ke server.
