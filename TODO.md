# TODO Ignis

Dokumen kerja untuk tim. Isi: (1) apa yang belum selesai, (2) struktur proyek dan fungsi tiap bagian, (3) langkah mengerjakan dan menyambungkan frontend dengan backend.
Terakhir diperbarui setelah penyederhanaan struktur dan penambahan auth (branch `backend`).

---

## 1. Yang belum selesai

Legenda: **BE** = `Ignis.Api`, **FE** = `Ignis.Frontend`. Prioritas: P1 = inti produk, P2 = penting, P3 = bisa belakangan.

### 1.1 Backend

| # | Item | Prioritas | Keadaan sekarang | Lokasi |
|---|---|---|---|---|
| B1 | Simulasi penyebaran api (cellular automata) | P1 | Stub, melempar `NotImplementedException`. Endpoint belum ada. | `Ignis.Api/Services/FireSpreadSimulator.cs` |
| B2 | Area pantauan | P2 | Entity dan tabel ada. Belum ada service dan endpoint. | `Domain/MonitoringArea.cs` |
| B3 | Notifikasi | P2 | Entity dan tabel ada. Belum ada service, endpoint, dan pengecekan hotspot baru per area. | `Domain/Notification.cs` |
| B4 | EF Core migration | P2 | Skema dibuat dengan `EnsureCreated`. Perubahan skema tidak bisa di-upgrade dengan aman. | `Data/DatabaseInitializer.cs` |
| B5 | Endpoint pengaturan (`/api/settings`) | P3 | API key FIRMS/OpenWeather hanya bisa diisi lewat `.env` server. | — |
| B6 | Ubah role pengguna (jadikan Admin) | P3 | Belum ada. Sekarang harus ubah langsung di database. | `Services/UserService.cs` |
| B7 | Pencabutan token / logout sisi server | P3 | Token JWT stateless, berlaku 60 menit. User yang dinonaktifkan baru tertolak saat login berikutnya. | `Services/TokenService.cs` |
| B8 | Pembatasan percobaan login (rate limiting) | P3 | Belum ada. | `Program.cs` |
| B9 | HTTPS dan konfigurasi deploy | P2 | Belum ada. Wajib sebelum dipakai di jaringan nyata (token dikirim lewat header). | — |
| B10 | Zona waktu | P3 | Semua waktu disimpan sebagai waktu lokal (`timestamp without time zone`). | `Data/AppDbContext.cs` |

### 1.2 Frontend

| # | Item | Prioritas | Keadaan sekarang | Lokasi |
|---|---|---|---|---|
| F1 | Dashboard peta utama memakai data nyata | P1 | Angka risiko (214/48/24/67/75), "Recent Locations", status darurat, dan pin peta masih dummy atau lokal. | `ViewModels/MainWindowViewModel.cs`, `Views/Dashboard/*` |
| F2 | Simulasi angin/asap di dashboard | P1 | Slider hanya animasi lokal. Menunggu B1. | `MainWindowViewModel.cs` (`SimulationProgress`) |
| F3 | Tombol logout | P2 | `IgnisApiClient.Logout()` ada, belum ada tombolnya. | `IgnisApiClient.cs`, header dashboard |
| F4 | Login ulang saat token kedaluwarsa | P2 | Setelah 60 menit panel admin menolak dengan pesan "Sesi berakhir", tanpa layar login otomatis. | `IgnisApiClient.SessionChanged` |
| F5 | Tampilan untuk user biasa (`Registered`) | P2 | User biasa hanya melihat pesan "hanya untuk Admin". Belum ada tampilan khusus. | `MainWindowViewModel.OpenAdminWindow` |
| F6 | Saved Locations, area pantauan, notifikasi di UI | P2 | Belum ada UI maupun API. Menunggu B2, B3. | — |
| F7 | Halaman Pengaturan | P3 | Isian API key FIRMS/weather tersimpan lokal tetapi tidak dipakai server. Sambungkan ke B5 atau hapus isiannya. | `Views/Admin/PengaturanPage.xaml.cs` |
| F8 | Setelan alamat API di UI | P3 | Alamat API hanya bisa diubah lewat env `IGNIS_API_URL` atau `settings.json`. | `Services/Admin/AppSettings.cs` |

### 1.3 Konfigurasi dan pengujian

| # | Item | Keterangan |
|---|---|---|
| K1 | Isi `Jwt__Key` | Minimal 32 karakter acak. Kosong = kunci acak sementara, semua user logout tiap server restart. Cara membuatnya ada di `.env.example`. |
| K2 | Kunci Google | `Google__ClientId` dan `Google__ClientSecret`. Tutorial: [docs/google-oauth.md](docs/google-oauth.md). |
| K3 | Key FIRMS dan OpenWeather | `Integrations__FirmsKey`, `Integrations__WeatherKey` (atau `OPENWEATHER_API_KEY`). Tanpa key, cuaca memakai data simulasi dan sync FIRMS dilewati. |
| K4 | Uji terhadap PostgreSQL nyata | Sequence Id dan `EnsureCreated` belum pernah dijalankan di lingkungan pengembangan ini. Tes otomatis memakai EF InMemory. |
| K5 | Uji sync FIRMS dengan key asli | `SyncService` dan `SyncBackgroundService` belum diuji dengan key sungguhan. |
| K6 | Uji OpenWeather asli | `WeatherClient.FetchAsync` belum diuji dengan key sungguhan. |
| K7 | Uji login Google | Pertukaran kode ke Google baru bisa diuji setelah K2. |
| K8 | Uji klik GUI | Alur login, registrasi, dan panel admin di jendela WPF baru diuji sampai aplikasi start tanpa crash. |
| K9 | Akun demo | `Storage__SeedDemoData=true` membuat akun dengan password `Ignis@123`. Jangan aktifkan di server sungguhan. |

---

## 2. Struktur proyek

```
Ignis.slnx
├── Ignis.Api/          server: logika bisnis, database, login, panggilan ke layanan luar
├── Ignis.Frontend/     aplikasi desktop WPF (tampilan saja, data lewat HTTP)
├── Ignis.Shared/       kontrak bersama: enum, DTO (bentuk JSON), Region
├── Ignis.Tests/        unit test dan tes end-to-end
├── database/schema.sql skrip tabel + sequence Id (dipakai docker-compose)
├── docs/               ERD, diagram kelas, fitur OOP, tutorial Google
├── docker-compose.yml  PostgreSQL untuk pengembangan
└── .env.example        daftar pengaturan server (salin ke .env)
```

Dependensi: `Frontend → Shared ← Api`, `Tests → Api, Shared`. Frontend tidak pernah merujuk `Ignis.Api`; keduanya hanya berbagi `Ignis.Shared`.

Alur satu permintaan: `View/ViewModel (FE)` → `IgnisApiClient` → HTTP + token JWT → `Endpoints/` → `Services/` → `Data/AppDbContext` → PostgreSQL.

### 2.1 `Ignis.Api`

#### `Program.cs`
Titik masuk. Hanya merangkai: baca konfigurasi, daftarkan layanan, pasang middleware (error, autentikasi, otorisasi), dan petakan route. Tidak ada logika bisnis. Berakhir dengan `public partial class Program` supaya tes bisa memakai `WebApplicationFactory`.

#### `Endpoints/` (route HTTP, tipis)
| Class | Fungsi |
|---|---|
| `AuthEndpoints` | `POST /api/auth/register`, `/login`, `/google`; `GET /api/auth/google/config`; `GET /api/auth/me`. Publik kecuali `/me`. |
| `HotspotEndpoints` | Hotspot (`GET` semua/satu: login; `POST/PUT/DELETE`: Admin), cuaca, prediksi (`GET`: login; `POST` buat baru: Admin). |
| `AdminEndpoints` | `/api/users`, `/api/users/{id}/toggle-active`, `/api/activity`, `/api/sync`. Semua Admin. Memuat `ActivityRequest`. |
| `Policies` | Nama kebijakan otorisasi (`Admin`). |
| `Mappings` | Mengubah entity menjadi DTO (`ToDto()`). |

#### `Services/` (logika use case)
| Class | Fungsi |
|---|---|
| `AuthService` | Registrasi, login email+password, login Google. Hasilnya `AuthResult` (user + token). Melempar `AuthException` (HTTP 401) bila gagal. |
| `TokenService` | Membuat token JWT (klaim `sub`, `email`, `name`, `role`). Memuat `JwtOptions`. |
| `Passwords` | Hash dan verifikasi password (PBKDF2 bawaan ASP.NET Identity). |
| `HotspotService` | Ambil (filter sumber/status), tambah manual, ubah, hapus, dan impor hotspot satelit dengan deduplikasi. |
| `PredictionService` | Ambil/simpan cuaca per hotspot, hitung dan simpan prediksi. |
| `FwiPredictionModel` | Rumus prediksi (arah, kecepatan, luas, risiko) dari cuaca. |
| `UserService` | Daftar pengguna, aktif/nonaktifkan (admin aktif terakhir tidak boleh dinonaktifkan). |
| `SyncService` | Sinkronisasi: ambil CSV FIRMS dengan retry, impor, cek koneksi cuaca. Satu sync pada satu waktu. |
| `SyncBackgroundService` | Menjalankan `SyncService` berkala. Mati kecuali `Sync:Enabled=true`. |
| `ActivityLogService` | Catat dan baca log aktivitas ("Admin menambah hotspot ..."). |
| `FireSpreadSimulator` | **Stub** simulasi penyebaran api (lihat B1). Memuat `SimulationFrame`. |

#### `Domain/` (entity dan aturan bisnis, tanpa EF/HTTP)
| Class | Fungsi |
|---|---|
| `Hotspot` (abstrak) | Titik api. Setter privat, validasi di konstruktor/method. `IsEditable` dan `GetDetails()` abstrak (polimorfisme). |
| `SatelliteHotspot`, `ManualHotspot` | Turunan `Hotspot`. Satelit tidak bisa diubah/hapus; manual bisa (`UpdateReport`). |
| `User` (abstrak), `Admin`, `RegisteredUser` | Pengguna. `CanManageHotspots` polimorfik. Status aktif lewat `Activate()/Deactivate()`. |
| `WeatherData`, `Prediction` | Cuaca di sekitar hotspot dan hasil prediksi. |
| `MonitoringArea`, `Notification` | Belum dipakai endpoint (lihat B2, B3). |
| `DomainException`, `Guard` | Pelanggaran aturan bisnis (HTTP 400) dan pemeriksaan invarian bersama. |
| `Coordinate`, `Vector` | Value object. |

#### `Data/` (database)
| Class | Fungsi |
|---|---|
| `AppDbContext` | Pemetaan semua entity ke tabel PostgreSQL (snake_case, pewarisan tabel). `NextIdAsync` membuat Id (`HS-`, `WX-`, `PR-`, `U-`) dari sequence database; mode InMemory memakai penghitung proses. |
| `DatabaseInitializer` | Saat start: buat tabel dan sequence, isi data demo (opsional), buat admin pertama dari `Admin__*`. |
| `DemoData` | Data contoh hotspot, prediksi, dan pengguna. Password semua akun demo `Ignis@123`. |

#### `Integrations/` (panggilan ke luar)
| Class | Fungsi |
|---|---|
| `FirmsClient` | Ambil CSV hotspot NASA FIRMS untuk Indonesia (24 jam terakhir). |
| `FirmsCsvParser` | Ubah CSV FIRMS menjadi `SatelliteHotspot`. Baris tidak valid dilewati. |
| `WeatherClient` | OpenWeather bila ada key; bila tidak ada atau gagal, jatuh ke data simulasi. |
| `GoogleOAuthClient` | Tukar kode Google menjadi identitas terverifikasi. Memuat `GoogleOptions`. |
| `IntegrationOptions` | Pengaturan FIRMS/OpenWeather (bagian `Integrations`). |

#### `Support/`
`DotEnv` (baca `.env` dari folder kerja ke atas), `ErrorHandling` (`DomainException` → 400, `AuthException` → 401, format `{ "error": "..." }`).

### 2.2 `Ignis.Frontend`

| Folder / class | Fungsi |
|---|---|
| `Services/Backend/IgnisApiClient` | Satu-satunya pintu ke server. Menyimpan sesi login (token + `CurrentUser`, `IsLoggedIn`, `IsAdmin`, event `SessionChanged`) dan melampirkan token ke tiap permintaan. Singleton. Pesan error dibungkus `BackendException`. |
| `Services/Backend/GoogleSignIn` | Alur login Google: buat PKCE, buka browser, tangkap kode di `127.0.0.1`. |
| `Services/Admin/AdminWorkspace` | Cache data admin (hotspot, prediksi, pengguna, aktivitas) sebagai baris tampilan, plus event perubahan. Tanpa logika bisnis. |
| `Services/Admin/AppSettings` | Pengaturan lokal di `%AppData%\IgnisAdmin\settings.json` (alamat API, interval, key CARTO). |
| `ViewModels/Auth/*` | `LoginFormViewModel`, `RegisterViewModel`, `LoginWindowViewModel`: logika layar login/registrasi. |
| `ViewModels/MainWindowViewModel` | Dashboard peta utama. Mengatur login, gerbang panel admin (cek `IsAdmin`), dan data dashboard (**masih dummy**, lihat F1). |
| `Views/Admin/*` | Halaman admin: Dashboard, Kelola Hotspot, Semua Hotspot, Hotspot Manual, Riwayat, Pengguna, Pengaturan, form dan detail hotspot. |
| `Views/Dashboard/*`, `Views/Auth/*`, `Views/Shared/*` | Peta utama, layar login, dan kontrol bersama. |
| `Models/Admin/Rows.cs` | Pembungkus DTO untuk tampilan (`HotspotRow`, `PredictionRow`, `UserRow`). |
| `Navigation/DialogService` | Membuka jendela login, jendela admin, dan kotak pesan. |
| `App.xaml.cs` | Daftar DI. |

### 2.3 `Ignis.Shared`
`Enums.cs` (`Role`, `HotspotSource`, `HotspotStatus`, `RiskLevel`, `ConnectionState`), `Dtos.cs` (semua DTO dan request/response, termasuk auth), `Region.cs` (wilayah untuk filter di desktop).

### 2.4 `Ignis.Tests`
`DomainTests` (konsep OOP, aturan entity), `ServiceTests` (service di atas EF InMemory), `ParserAndModelTests` (parser FIRMS, model prediksi, cuaca simulasi), `AuthApiTests` (end-to-end: login, 401/403, registrasi, token rusak), `TestDb` (helper database tes). Jalankan: `dotnet test Ignis.Tests`.

### 2.5 Aturan akses endpoint (ringkas)

| Endpoint | Siapa |
|---|---|
| `/health`, `/api/auth/register`, `/login`, `/google`, `/google/config` | Publik |
| `GET /api/auth/me`, `GET /api/hotspots*`, `GET /api/predictions` | Pengguna login |
| `POST/PUT/DELETE /api/hotspots*`, `POST /api/hotspots/{id}/predictions`, `/api/users*`, `/api/activity`, `/api/sync` | Admin |

---

## 3. Langkah mengerjakan dan integrasi

Aturan umum untuk semua fitur baru: **(1)** entity/aturan di `Domain/` → **(2)** logika di `Services/` → **(3)** route di `Endpoints/` → **(4)** DTO di `Ignis.Shared/Dtos.cs` → **(5)** method di `IgnisApiClient` → **(6)** tampilan/ViewModel → **(7)** tes di `Ignis.Tests`. Jalankan `dotnet build Ignis.slnx` dan `dotnet test Ignis.Tests` sebelum commit.

### Langkah 0 — Siapkan lingkungan (sekali)
1. Salin `.env.example` ke `.env`. Isi `Jwt__Key` (K1). Untuk mulai cepat biarkan `Storage__Provider=InMemory` dan `Storage__SeedDemoData=true`.
2. `dotnet run --project Ignis.Api` lalu cek `http://localhost:5000/health`.
3. `dotnet run --project Ignis.Frontend`, login `admin@ignis.example` / `Ignis@123`.
4. Uji PostgreSQL (K4): `docker compose up -d`, di `.env` set `Storage__Provider=Postgres` dan `ConnectionStrings__Ignis=...`, jalankan API lagi. Periksa tabel `hotspots` dan hasil `SELECT nextval('ignis_hotspot_seq')`. Tambah hotspot dari UI dan pastikan Id-nya `HS-1000`, dst.

### Langkah 1 — Sambungkan dashboard peta utama ke data nyata (F1) · P1
Tujuan: angka risiko, pin hotspot, dan daftar lokasi berasal dari API.
1. Pakai endpoint yang sudah ada: `GET /api/hotspots` dan `GET /api/predictions`.
2. Pada `MainWindowViewModel`, ganti nilai hardcode (`TotalMonitoredNodes`, `LowRiskCount`, ... `CriticalRiskCount`) dengan hitungan dari `PredictionDto.Risk` terbaru per hotspot.
3. Isi pin peta dari `HotspotDto` (`Latitude`, `Longitude`, `Status`, `Source`) pada `MapCanvasPlaceholder`/`MapControlsOverlay`. Pertimbangkan memakai `AdminWorkspace` agar cache dibagi dengan panel admin, atau buat service ringan khusus dashboard.
4. Ganti "Captain R. Miller" dan "Incident Commander" dengan `IgnisApiClient.CurrentUser` (sebagian sudah lewat `ApplySession`).
5. Dashboard ini bisa dibuka user biasa, jadi cukup butuh token (tidak perlu Admin). Bila belum login, tampilkan ajakan login atau data kosong, bukan error.
6. Uji: login sebagai user biasa dan admin, bandingkan angka dengan isi tabel `hotspots`/`predictions`.

### Langkah 2 — Login, logout, dan sesi (F3, F4, F5) · P2
1. **Logout (F3)**: tambah tombol di header dashboard yang memanggil `IgnisApiClient.Logout()`. `MainWindowViewModel.ApplySession` sudah mereset `IsLoggedIn`; reset juga `UserName`/`UserRole` ke kosong.
2. **Token kedaluwarsa (F4)**: `IgnisApiClient` sudah memicu `SessionChanged` dan melempar "Sesi berakhir". Tangani di `AdminMainWindow`: saat sesi berakhir, tutup jendela admin lalu panggil `IDialogService.ShowLoginDialog()`.
3. **User biasa (F5)**: tentukan apa yang boleh dilihat (peta dan prediksi, tanpa panel admin), lalu sembunyikan tombol "Admin/Settings" bila `!IsAdmin` (bind ke properti baru di `MainWindowViewModel`).
4. Uji manual: login → tunggu/ubah `Jwt:ExpiresMinutes` jadi 1 → pastikan pesan dan layar login muncul.

### Langkah 3 — Simulasi penyebaran api (B1, F2) · P1
1. **Domain/Service**: isi `FireSpreadSimulator.Simulate(origin, weather, hours)`. Rencana: grid sel di sekitar titik asal; tiap jam sel terbakar menyulut tetangganya dengan peluang yang bergantung pada kecepatan dan arah angin (`WeatherData.GetWindVector()`), kelembapan, dan bahan bakar. Keluaran satu `SimulationFrame` per jam. Buat deterministik (terima seed) agar mudah diuji.
2. **Tes dulu**: tambah `Ignis.Tests/SimulationTests.cs` (angin dari barat → api merambat ke timur, luas tidak mengecil, hasil sama untuk input sama).
3. **Endpoint**: `GET /api/hotspots/{id}/simulation?hours=N` di `HotspotEndpoints` (user login). Ambil cuaca lewat `PredictionService.GetWeatherAsync`, jalankan simulator, kembalikan frame.
4. **DTO**: tambah `SimulationFrameDto` (`HourOffset`, daftar koordinat, `AreaHa`) di `Ignis.Shared/Dtos.cs`. Batasi `hours` (mis. 1–24) dan jumlah sel agar respons tidak membengkak.
5. **Frontend**: tambah `GetSimulationAsync(hotspotId, hours)` di `IgnisApiClient`. Hubungkan slider `SimulationProgress` di `MainWindowViewModel` ke frame dan gambar sel terbakar di peta.

### Langkah 4 — Area pantauan dan notifikasi (B2, B3, F6) · P2
1. **Area pantauan**: buat `MonitoringAreaService` (tambah, daftar milik user, hapus). Validasi lintang/bujur di entity (ikuti pola `Guard` pada `Hotspot`). Id dari `NextIdAsync` (tambahkan `IdKind.Area` dan sequence-nya di `AppDbContext` dan `schema.sql`).
2. **Endpoint**: `/api/monitoring-areas` (GET, POST, DELETE), hanya pemilik. Ambil `userId` dari klaim `sub` di token, bukan dari body request.
3. **Notifikasi**: `NotificationService` dengan `CheckNewHotspotsAsync(userId, since)` (hotspot dengan `DetectedAt > since` di dalam `MonitoringArea.Contains`) dan penyimpanan `Notification`. Panggil setelah `SyncService` mengimpor hotspot baru.
4. **Endpoint**: `GET /api/notifications` dan `POST /api/notifications/{id}/read`. Mulai dengan polling dari frontend (timer per beberapa menit); SignalR bisa belakangan.
5. **Frontend**: layar kelola area, ikon lonceng dengan jumlah belum dibaca, dan isi daftar "Saved Locations". Hormati `AppSettings.NotificationsEnabled`.
6. **Tes**: area mengandung titik, notifikasi hanya untuk pemilik area, user lain tidak bisa membaca/menghapus area orang lain (403/404).

### Langkah 5 — Migration dan skema (B4) · P2
1. Pasang `dotnet-ef` dan tambah paket `Microsoft.EntityFrameworkCore.Design` di `Ignis.Api`.
2. Buat migration awal dari model saat ini: `dotnet ef migrations add Awal --project Ignis.Api`. Pastikan hasilnya sama dengan `database/schema.sql`.
3. Ganti `EnsureCreatedAsync` di `DatabaseInitializer` dengan `MigrateAsync`. Pertahankan pembuatan sequence Id (masukkan ke migration atau biarkan `IF NOT EXISTS`).
4. Perhatikan: database yang sudah dibuat dengan `EnsureCreated` tidak punya tabel `__EFMigrationsHistory`. Untuk database dev, hapus volume dan buat ulang (`docker compose down -v`).
5. Setelah itu, setiap perubahan entity = satu migration baru.

### Langkah 6 — Pengaturan dan keamanan (B5–B8, F7, F8) · P3
1. **Pengaturan (B5, F7)**: pilih satu. (a) Sediakan `GET/PUT /api/settings` (Admin) yang menyimpan key FIRMS/weather di database terenkripsi, lalu `HotspotSync` membaca dari sana; atau (b) hapus isian key dari `PengaturanPage` dan jelaskan bahwa key diisi di `.env` server. (b) lebih sederhana dan aman.
2. **Role (B6)**: `PUT /api/users/{id}/role` (Admin). Aturan: admin aktif terakhir tidak boleh diturunkan (pola sama dengan `ToggleActiveAsync`). Mengubah role user EF dengan pewarisan tabel (TPT) tidak bisa lewat satu entity; hapus dan buat ulang baris di tabel anak, atau ganti desain role menjadi kolom biasa.
3. **Cabut token (B7)**: tambah klaim versi (`token_version`) di `User` dan token, lalu periksa di event `OnTokenValidated` JwtBearer. Naikkan versi saat user dinonaktifkan atau logout.
4. **Rate limiting (B8)**: pakai `AddRateLimiter` bawaan ASP.NET untuk `/api/auth/login` dan `/register`.
5. **HTTPS (B9)**: pasang di reverse proxy (Caddy/Nginx) atau Kestrel dengan sertifikat, lalu set `IGNIS_API_URL` di desktop ke `https://...`.
6. **Alamat API (F8)**: tambah isian "Alamat server" di halaman Pengaturan yang menyimpan `AppSettings.ApiBaseUrl`.

### Langkah 7 — Login Google (K2, K7) · setelah kunci siap
1. Ikuti [docs/google-oauth.md](docs/google-oauth.md): buat proyek, consent screen, tambah diri sebagai Test user, buat OAuth client **Desktop app**.
2. Isi `Google__ClientId` dan `Google__ClientSecret` di `.env`, restart API. Cek `GET /api/auth/google/config` mengembalikan Client ID.
3. Jalankan desktop, klik tombol Google, selesaikan di browser. Pastikan masuk sebagai `Registered`.
4. Bila gagal, lihat tabel "Masalah umum" di dokumen itu, dan laporkan pesan error aslinya.

### Langkah 8 — Rilis dan distribusi
1. Pastikan K1–K9 beres. Server: `Storage__SeedDemoData=false`, `Admin__Email`/`Admin__Password` terisi, `Jwt__Key` terisi, HTTPS aktif.
2. Publish server: `dotnet publish Ignis.Api -c Release -r linux-x64 --self-contained -o publish/api` (atau `win-x64`).
3. Publish desktop: `dotnet publish Ignis.Frontend -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/frontend`. Tambahkan `<ApplicationIcon>` dan ubah ikon PNG menjadi `Resource` bila perlu.
4. Desktop butuh server yang berjalan. Beri tahu pengguna alamat server, atau isi default `ApiBaseUrl` di `AppSettings` sebelum publish.
5. Uji di komputer bersih: install, login, tambah hotspot, sync.

### Urutan pengerjaan yang disarankan
`Langkah 0` → `1` (dashboard nyata) → `2` (logout/sesi) → `3` (simulasi) → `4` (area dan notifikasi) → `5` (migration) → `6` → `7` (Google, kapan pun kunci siap) → `8`.
