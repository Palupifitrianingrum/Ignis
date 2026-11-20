IGNIS</br>
Aplikasi desktop C# yang memetakan hotspot kebakaran secara real-time, menganalisis kondisi lingkungan, kemudian memperkirakan arah dan potensi penyebaran api dalam beberapa jam ke depan</br></br>
IGNIS SQUAD</br>
Ketua Kelompok: Natanael Sebastian Simanjuntak-24/542676/TK/60273</br>
Anggota 1: Muhammad Nafal Zakin Rustanto-24/535255/TK/59364</br>
Anggota 2: Wahyu Fajrin Adi Putra 24/548043/TK/59664</br>
Anggota 3: Palupi Fitria Ningrum-24/540125/TK/59904</br>
Anggota 4: Natanael Sebastian Simanjuntak-24/542676/TK/60273</br>

## Struktur solution

Hanya dua aplikasi yang berjalan: **server** (`Ignis.Api`) dan **aplikasi desktop** (`Ignis.Frontend`). Dua proyek lainnya pendukung.

| Proyek | Peran |
|---|---|
| `Ignis.Api` | Server ASP.NET Core: semua logika bisnis, database (PostgreSQL), login/JWT, dan panggilan ke NASA FIRMS/OpenWeather/Google. |
| `Ignis.Frontend` | Aplikasi desktop WPF (View + ViewModel). Tidak punya logika bisnis; semua data lewat HTTP ke `Ignis.Api`. |
| `Ignis.Shared` | Kontrak bersama server dan desktop: enum, DTO (bentuk JSON), `Region`. Tanpa dependensi lain. |
| `Ignis.Tests` | Unit test domain/service dan tes end-to-end API (login, otorisasi). |

Dependensi: `Frontend → Shared ← Api`, `Tests → Api, Shared`. Frontend tidak pernah merujuk `Ignis.Api`.

### Peta kode: class itu ada di mana?

**`Ignis.Api/`** (satu folder = satu peran)

| Folder | Isi | Class |
|---|---|---|
| `Program.cs` | Titik masuk; hanya merangkai layanan, middleware, dan route | — |
| `Endpoints/` | Definisi route HTTP (tipis, memanggil Services) | `AuthEndpoints`, `HotspotEndpoints`, `AdminEndpoints`, `Policies`, `Mappings` (entity → DTO) |
| `Services/` | Logika use case | `AuthService`, `TokenService`, `Passwords`, `HotspotService`, `PredictionService`, `UserService`, `SyncService` (+ `SyncBackgroundService`), `ActivityLogService`, `FwiPredictionModel`, `FireSpreadSimulator` (simulasi penyebaran api) |
| `Domain/` | Entity dan aturan bisnis murni (tanpa EF/HTTP) | `Hotspot` → `SatelliteHotspot`/`ManualHotspot`; `User` → `Admin`/`RegisteredUser`; `WeatherData`, `Prediction`, `MonitoringArea`, `Notification`, `DomainException`, `Coordinate`, `Vector` |
| `Data/` | Database | `AppDbContext` (pemetaan tabel + generator Id), `DatabaseInitializer` (buat tabel, seed, admin pertama), `DemoData` |
| `Integrations/` | Panggilan ke layanan luar | `FirmsClient`, `FirmsCsvParser`, `WeatherClient`, `GoogleOAuthClient`, `IntegrationOptions`, `IntegrationException`/`IntegrationStatus` |
| `Support/` | Pendukung kecil | `DotEnv` (baca `.env`), `ErrorHandling` (exception → HTTP 400/401) |

**`Ignis.Frontend/`**

| Folder | Isi |
|---|---|
| `Views/` | Tampilan XAML (`Admin/`, `Auth/`, `Dashboard/`, `Shared/`) |
| `ViewModels/` | Logika tampilan (`MainWindowViewModel`, `Auth/LoginFormViewModel`, `Auth/RegisterViewModel`, ...) |
| `Services/Backend/` | `IgnisApiClient` (satu-satunya pintu ke server + sesi login), `GoogleSignIn` (buka browser untuk login Google) |
| `Services/Admin/` | `AdminWorkspace` (cache data admin), `AppSettings` (pengaturan lokal) |
| `Models/` | Baris tampilan (`Admin/Rows.cs`) dan model dashboard |
| `Navigation/` | `DialogService` (buka jendela login/admin) |

Diagram kelas: [docs/class-diagram.md](docs/class-diagram.md).

## Menjalankan

```bash
dotnet build Ignis.slnx
dotnet test Ignis.Tests
```

**A. Tanpa database (data demo di memori)**

```bash
dotnet run --project Ignis.Api        # API di http://localhost:5000
dotnet run --project Ignis.Frontend   # desktop (terminal kedua)
```

Login admin: `admin@ignis.example` / `Ignis@123` (akun demo lain: `andi.pratama@example.com`, password sama). Data hilang saat API berhenti.

**B. Dengan PostgreSQL**

1. Nyalakan PostgreSQL dan buat database `ignis` (pilih salah satu):
   - Docker dari terminal: salin `.env.example` ke `.env`, isi `IGNIS_DB_PASSWORD`, lalu `docker compose up -d`.
   - PostgreSQL yang sudah terpasang di laptop: buat database `ignis` beserta user-nya. Skema dibuat otomatis oleh API; skrip setara ada di `database/schema.sql`.
2. Di `.env` (root repo) isi `Storage__Provider=Postgres`, `ConnectionStrings__Ignis=...`, dan `Storage__SeedDemoData=true` (data contoh + akun demo) **atau** `Admin__Email` dan `Admin__Password` (admin pertama tanpa data demo).
3. `dotnet run --project Ignis.Api` (tabel dan sequence Id dibuat otomatis), lalu cek `http://localhost:5000/health`.
4. `dotnet run --project Ignis.Frontend` dan login. Alamat API default `http://localhost:5000/`; ubah lewat env `IGNIS_API_URL` atau `%AppData%\IgnisAdmin\settings.json`.
5. Buka **Kelola Hotspot**, klik **Tambah Hotspot**, lalu cek di database: `SELECT * FROM hotspots h JOIN manual_hotspots m USING (hotspot_id);`

Dokumentasi: [skema dan ERD](docs/database.md), [fitur untuk tugas OOP](docs/oop-feature.md), [diagram kelas](docs/class-diagram.md), [login Google](docs/google-oauth.md), [simulasi penyebaran api](docs/simulasi-api.md), [key dan integrasi NASA FIRMS/OpenWeather](docs/integrasi-data.md).

## Keamanan

- **Login**: email + password (hash PBKDF2) atau Google. Server menerbitkan token JWT (berlaku 60 menit, `Jwt:ExpiresMinutes`); desktop melampirkannya di setiap permintaan.
- **Otorisasi**: semua `/api/*` butuh token. Baca data = pengguna mana pun yang login. Tambah/ubah/hapus hotspot, kelola pengguna, log aktivitas, dan sinkronisasi = hanya role `Admin`.
- **API key layanan luar** (FIRMS, OpenWeather) bisa diganti admin dari halaman Pengaturan: disimpan terenkripsi di server, diuji dulu, dan tidak pernah ditampilkan lagi; key `.env` tetap jadi default ([detail](docs/integrasi-data.md)).
- **Registrasi publik** selalu menjadi `Registered`, tidak pernah `Admin`. Admin dibuat lewat seed demo atau `Admin__Email`/`Admin__Password`.
- **Wajib di server sungguhan**: isi `Jwt__Key` (≥ 32 karakter), jangan nyalakan `Storage__SeedDemoData`, dan pasang HTTPS (token dikirim sebagai header, jadi tanpa HTTPS bisa disadap).
- Batasan yang diketahui: token tidak bisa dicabut sebelum kedaluwarsa (akun yang dinonaktifkan baru tertolak saat login berikutnya), dan belum ada pembatasan percobaan login.

## Status backend (yang belum selesai)

Tanda `TODO(backend)` di kode menandai hal berikut:
- Notifikasi dan area pantauan — entity ada di `Domain/`, belum ada layanan dan endpoint.
- EF Core migration (skema dibuat dengan `EnsureCreated`; untuk produksi ganti dengan migration).
- Isian API key FIRMS/weather di halaman **Pengaturan** desktop belum dipakai: server membaca key dari konfigurasinya sendiri (`Integrations__*`). Sediakan `/api/settings` atau hapus isian itu.
- Pencabutan token (logout di sisi server) dan pembatasan percobaan login.
- Pengambilan data FIRMS dan OpenWeather sudah diuji dengan HTTP palsu dan **dengan key sungguhan** (1.010 hotspot nyata diimpor dan diperiksa, cuaca nyata dipakai simulasi). Cara mendapatkan dan memasang key ada di [docs/integrasi-data.md](docs/integrasi-data.md); `GET /api/integrations/status` (admin) memeriksa key kapan saja. Yang tersisa: uji dengan PostgreSQL sungguhan.
- Jalur PostgreSQL (sequence Id, `EnsureCreated`) belum dijalankan terhadap server PostgreSQL nyata di lingkungan pengembangan ini; tes otomatis memakai EF InMemory.
