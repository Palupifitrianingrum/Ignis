# TODO Ignis

Dokumen kerja untuk tim. Isi: (1) apa yang belum selesai, (2) struktur proyek dan fungsi tiap bagian, (3) langkah mengerjakan dan menyambungkan frontend dengan backend.
Terakhir diperbarui 2026-11-21 (setelah simulasi api diselesaikan dan dihubungkan ke frontend). Build berhasil dan 179 tes lulus.

Catatan jujur tentang status: "SELESAI" di bawah berarti kodenya ada dan terbaca konsisten, plus lulus tes otomatis bila ada.
Tampilan dashboard dan simulasi sudah diperiksa dengan menjalankan aplikasi sungguhan terhadap API sungguhan lalu mengambil screenshot jendela (lihat K8). Klik langsung pada pin peta dan alur login/registrasi lewat formulir belum diuji otomasi, jadi butir bertanda "perlu uji GUI" masih harus dicoba manual.

---

## 0. Ringkasan perubahan sejak TODO sebelumnya

| Yang dulu belum selesai | Sekarang |
|---|---|
| B1 Simulasi penyebaran api | **Selesai**: model diperbaiki dan dikalibrasi, endpoint mengembalikan hasil lengkap (`SimulationDto`), 29 tes baru. Dokumentasi: [docs/simulasi-api.md](docs/simulasi-api.md). |
| F1 Dashboard peta utama data nyata | **Selesai dan kini benar-benar tampil.** Catatan koreksi: sebelumnya data dimuat tetapi `FireRiskCard` (angka risiko dan kontrol simulasi) tidak pernah dipasang di `MainWindow`, jadi tidak terlihat. Sudah dipasang dan diverifikasi lewat screenshot. |
| F2 Simulasi di dashboard | **Selesai**: pilih hotspot lewat klik pin atau Recent Locations, putar per jam, visualisasi di peta dan panel (kompas, luas, grafik, legenda). |
| F3 Tombol logout | **Selesai**: tombol Logout di sidebar. |
| F4 Login ulang saat token kedaluwarsa | **Selesai** (perlu uji GUI): jendela admin menutup dan membuka login saat sesi berakhir; dashboard dikosongkan. |
| F5 Tampilan user biasa | **Sebagian**: tombol Admin disembunyikan bila bukan Admin. |
| F8 Alamat API di UI | **Selesai**: jendela Pengaturan pengguna (`UserSettingsWindow`). Ada catatan restart (lihat F8). |

Prioritas baru: **paket rilis untuk penilaian** (B11), karena tugas dinilai dengan mengirim .exe dan backend.

---

## 1. Yang belum selesai

Legenda: **BE** = `Ignis.Api`, **FE** = `Ignis.Frontend`. Prioritas: P1 = harus ada untuk penilaian, P2 = penting, P3 = bisa belakangan.

### 1.1 Backend

| # | Item | Prioritas | Keadaan sekarang | Lokasi |
|---|---|---|---|---|
| B11 | **Paket rilis untuk penilaian** | P1 | Belum ada. Penilai menerima .exe desktop dan backend untuk dijalankan sendiri, dengan PostgreSQL dan semua key dari Anda. Pilihan: PostgreSQL online (Neon/Supabase) atau PostgreSQL portable yang dijalankan `.bat` (tanpa Docker). Lihat Langkah 1. | — |
| B1 | Simulasi penyebaran api | **Selesai** | Cellular automata deterministik; arah angin diperbaiki (sebelumnya terbalik: api merambat ke arah asal angin); angin tenang merata; laju dikalibrasi; batas 400 sel dilaporkan (`truncated`); luas sel dikoreksi menurut lintang. Endpoint `GET /api/hotspots/{id}/simulation` mengembalikan kondisi cuaca, arah, ukuran sel, dan frame per jam. Tes perilaku dan endpoint lulus. Keterbatasan yang disengaja (tanpa data lahan/medan) tercatat di dokumen simulasi. | `Services/FireSpreadSimulator.cs`, `Ignis.Tests/SimulationTests.cs` |
| B2 | Area pantauan | P2 | Entity dan tabel ada. Belum ada service dan endpoint. | `Domain/MonitoringArea.cs` |
| B3 | Notifikasi | P2 | Entity dan tabel ada. Belum ada service, endpoint, dan pengecekan hotspot baru per area. | `Domain/Notification.cs` |
| B4 | EF Core migration | P2 | Skema dibuat dengan `EnsureCreated`. Perubahan skema tidak bisa di-upgrade dengan aman. | `Data/DatabaseInitializer.cs` |
| B9 | HTTPS dan konfigurasi deploy | P3 untuk prototipe | Belum ada. Wajib sebelum dipakai di jaringan nyata (token dikirim lewat header). Untuk penilaian lokal (`localhost`) bisa dilewati, tetapi sebutkan di laporan. | — |
| B5 | Endpoint pengaturan (`/api/settings`) | P3 | API key FIRMS/OpenWeather hanya bisa diisi lewat `.env` server. | — |
| B6 | Ubah role pengguna (jadikan Admin) | P3 | Belum ada. Sekarang harus ubah langsung di database. | `Services/UserService.cs` |
| B7 | Pencabutan token / logout sisi server | P3 | Token JWT stateless, berlaku 60 menit. User yang dinonaktifkan baru tertolak saat login berikutnya. | `Services/TokenService.cs` |
| B8 | Pembatasan percobaan login (rate limiting) | P3 | Belum ada. | `Program.cs` |
| B10 | Zona waktu | P3 | Semua waktu disimpan sebagai waktu lokal server (`timestamp without time zone`). `acq_time` FIRMS (UTC) kini diubah ke waktu lokal server saat impor, jadi server berzona UTC akan menyimpan UTC. Indonesia punya tiga zona (WIB/WITA/WIT). | `Data/AppDbContext.cs`, `Integrations/FirmsCsvParser.cs` |
| B12 | Status integrasi di UI | P3 | Server punya `GET /api/integrations/status` (admin), tetapi desktop belum menampilkannya. Tambahkan `IgnisApiClient.GetIntegrationStatusAsync` dan tampilkan di halaman Pengaturan admin (sekaligus menggantikan isian key yang tidak dipakai, F7). | `IgnisApiClient.cs`, `PengaturanPage.xaml.cs` |
| B13 | Sinkronisasi lama di desktop | P3 | Klien desktop membatasi tiap permintaan 60 detik. Sinkronisasi normal selesai dalam hitungan detik, tetapi bila FIRMS lambat (timeout 25 detik × percobaan ulang) bisa melewatinya; server tetap menyelesaikannya. Bila perlu, jadikan sinkronisasi tugas latar dengan status yang di-poll. | `Services/SyncService.cs` |

### 1.2 Frontend

| # | Item | Prioritas | Keadaan sekarang | Lokasi |
|---|---|---|---|---|
| F1 | Dashboard peta utama data nyata | Selesai, perlu uji GUI | Angka risiko dan daftar lokasi kini tampil (`FireRiskCard` dipasang di kiri-bawah). **Bila hotspot belum punya prediksi, risikonya ditebak dari `Confidence`** (≥85 Critical, ≥70 High, ≥50 Medium), jadi angkanya bukan prediksi sungguhan. Bedakan di laporan atau beri label. | `ViewModels/MainWindowViewModel.cs` (`ApplyFiltersAndRefreshView`) |
| F2 | Simulasi di dashboard | Selesai | Klik pin peta atau item Recent Locations memilih target; hasil diputar otomatis, digambar sebagai kotak sel berwarna menurut umur api, plus panel visualisasi. Memilih hotspot lain menghapus hasil lama; pilihan baru membatalkan permintaan yang tertunda; error server ditampilkan di panel. | `ViewModels/MainWindowViewModel.Simulation.cs`, `Views/Dashboard/SimulationPanel.xaml`, `MapCanvasPlaceholder.xaml.cs` |
| F9 | Pesan error saat API tidak terjangkau | P2 | `LoadDashboardDataAsync` menelan semua error dan hanya mengosongkan data, jadi pengguna melihat dashboard kosong tanpa tahu sebabnya. Tampilkan pesan ("Server tidak terjangkau") dan tombol coba lagi. | `MainWindowViewModel.cs` |
| F10 | Uji klik pin peta | P3 | Klik pin memanggil `SelectSimulationTargetAsync` (jalur kode sama dengan Recent Locations yang sudah diuji lewat screenshot), tetapi klik langsung pada peta Mapsui belum diuji otomasi. Coba manual: klik pin, panel dan sel muncul; klik pin lain, hasil berganti. | `MapCanvasPlaceholder.xaml.cs` (`OnMapInfo`) |
| F6 | Saved Locations, area pantauan, notifikasi di UI | P2 | **`SavedLocationsCount` hanya jumlah Recent Locations**, bukan lokasi yang disimpan pengguna. Belum ada UI maupun API area/notifikasi. Menunggu B2, B3. Opsi `NotificationsEnabled` sudah ada di pengaturan tetapi belum ada notifikasinya. | `MainWindowViewModel.cs`, `Views/Dashboard/SidebarNav.xaml` |
| F4 | Login ulang saat token kedaluwarsa | Selesai, perlu uji GUI | `AdminMainWindow` menutup dirinya dan membuka login saat sesi berakhir; dashboard dikosongkan lewat `ApplySession`. Uji dengan `Jwt:ExpiresMinutes=1`. | `Views/Admin/AdminMainWindow.xaml.cs` |
| F5 | Tampilan untuk user biasa (`Registered`) | P3 | Tombol Admin disembunyikan lewat `IsAdmin`. User biasa masih bisa melihat peta dan prediksi, itu sudah sesuai. Tinggal uji dengan akun `andi.pratama@example.com`. | `Views/Dashboard/SidebarNav.xaml` |
| F7 | Halaman Pengaturan admin | P3 | Isian API key FIRMS/weather tersimpan lokal tetapi **tidak dipakai server** (server membaca `.env`). Sambungkan ke B5 atau hapus isiannya. Isian CARTO dan notifikasi kini juga ada di jendela Pengaturan pengguna (duplikat). | `Views/Admin/PengaturanPage.xaml.cs` |
| F8 | Alamat API di Pengaturan | Selesai, ada catatan | `UserSettingsWindow` menyimpan `ApiBaseUrl`, tetapi `HttpClient` dibuat sekali saat start (`App.xaml.cs`), jadi **perubahan alamat baru berlaku setelah aplikasi dibuka ulang**, dan env `IGNIS_API_URL` menimpa isian ini. Tambahkan teks "mulai ulang aplikasi" di jendela itu. | `Views/Dashboard/UserSettingsWindow.xaml.cs` |

### 1.3 Konfigurasi, pengujian, dan kebersihan repo

| # | Item | Keterangan |
|---|---|---|
| K1 | Isi `Jwt__Key` | Minimal 32 karakter acak. Kosong = kunci acak sementara, semua user logout tiap server restart. Cara membuatnya ada di `.env.example`. |
| K2 | Kunci Google | `Google__ClientId` dan `Google__ClientSecret`. Tutorial: [docs/google-oauth.md](docs/google-oauth.md). |
| K3 | Key FIRMS dan OpenWeather | **Terpasang dan terverifikasi (2026-11-21).** Tutorial: [docs/integrasi-data.md](docs/integrasi-data.md). `GET /api/integrations/status` melaporkan `Connected` untuk keduanya. Tanpa key: sinkronisasi hotspot dilewati dan cuaca memakai data simulasi. |
| K4 | **Uji terhadap PostgreSQL nyata (wajib sebelum rilis)** | Sequence Id (`nextval`, termasuk `NextIdsAsync` untuk impor massal) dan `EnsureCreated` belum pernah dijalankan terhadap PostgreSQL sungguhan. Pada 2026-11-21 `.env` sudah menunjuk `Storage__Provider=Postgres` di `127.0.0.1:5432`, tetapi tidak ada yang mendengarkan di port itu (Docker Desktop terpasang, mesinnya belum menyala), sehingga API gagal start. Nyalakan Docker Desktop lalu `docker compose up -d`, atau set `Storage__Provider=InMemory` sementara. Ini penentu paket penilaian (B11). |
| K5 | Uji sync FIRMS dengan key asli | **Selesai (2026-11-21)**: 1.010 hotspot nyata diimpor (NOAA-20 dan NOAA-21), jam deteksi benar (UTC → WIB), idempoten. Temuan: `DAY_RANGE=1` hanya mencakup tanggal UTC hari ini, jadi default dinaikkan ke 2; Suomi NPP NRT berhenti sejak 2026-11-02 (dijelaskan otomatis di pesan sinkronisasi). Tersisa: uji impor besar di PostgreSQL sungguhan (K4) dan sinkronisasi terjadwal berjam-jam. |
| K6 | Uji OpenWeather asli | **Selesai (2026-11-21)**: cuaca sungguhan terbaca (satuan metrik, arah angin asal), dipakai simulasi (`condition` bukan "Simulasi"). |
| K7 | Uji login Google | Pertukaran kode ke Google baru bisa diuji setelah K2. |
| K8 | Uji klik GUI | Dashboard, simulasi (jam 0/9/18), dan keadaan server mati sudah diperiksa lewat screenshot aplikasi sungguhan. **Belum diuji otomasi**: formulir login/registrasi, klik pin peta, panel admin, dan Pengaturan. Lakukan satu putaran manual (daftar periksa di Langkah 2). |
| K9 | Akun demo | Default: `SeedDemoData` aktif untuk InMemory dan mati untuk Postgres. Akun demo memakai password `Ignis@123`. Untuk paket penilaian Postgres, set `Storage__SeedDemoData=true` agar penilai langsung melihat data. |
| K10 | Bersihkan file lepas | `Ignis.Tests/TmpAccessDemo.cs` (kosong, tidak dipakai, belum di-commit): hapus. |
| K11 | Commit dokumen OOP | `docs/konsep-oop.md` (penjelasan inheritance/polymorphism/encapsulation) belum di-commit. Nomor barisnya bisa bergeser bila `Hotspot.cs` berubah. |
| K12 | README sudah usang di dua baris | [README.md](README.md) masih menulis `FireSpreadSimulator (stub)` dan "Simulasi penyebaran api … (stub)" di bagian Status backend. Perbarui sesuai B1. |
| K13 | Periksa hasil impor di tampilan | Key sudah terverifikasi lewat API. Tersisa pengecekan visual di aplikasi desktop: jalankan Sinkronisasi dari panel admin, lihat pin hotspot baru di peta (ratusan titik; periksa performa Mapsui), klik satu pin lalu pastikan jam, satelit, dan keyakinan tampil benar, dan buka simulasinya. |

---

## 2. Struktur proyek

```
Ignis.slnx
├── Ignis.Api/          server: logika bisnis, database, login, panggilan ke layanan luar
├── Ignis.Frontend/     aplikasi desktop WPF (tampilan saja, data lewat HTTP)
├── Ignis.Shared/       kontrak bersama: enum, DTO (bentuk JSON), Region
├── Ignis.Tests/        unit test dan tes end-to-end (179 tes)
├── database/schema.sql skrip tabel + sequence Id (dipakai docker-compose)
├── docs/               ERD, diagram kelas, fitur OOP, konsep OOP, simulasi api, integrasi data (tutorial key), tutorial Google
├── docker-compose.yml  PostgreSQL untuk pengembangan
└── .env.example        daftar pengaturan server (salin ke .env)
```

Dependensi: `Frontend → Shared ← Api`, `Tests → Api, Shared`. Frontend tidak pernah merujuk `Ignis.Api`; keduanya hanya berbagi `Ignis.Shared`.

Alur satu permintaan: `View/ViewModel (FE)` → `IgnisApiClient` → HTTP + token JWT → `Endpoints/` → `Services/` → `Data/AppDbContext` → PostgreSQL.

### 2.1 `Ignis.Api`

#### `Program.cs`
Titik masuk. Hanya merangkai: baca konfigurasi, daftarkan layanan (termasuk `FireSpreadSimulator` sebagai singleton), pasang middleware (error, autentikasi, otorisasi), dan petakan route. Tidak ada logika bisnis. Berakhir dengan `public partial class Program` supaya tes bisa memakai `WebApplicationFactory`.

#### `Endpoints/` (route HTTP, tipis)
| Class | Fungsi |
|---|---|
| `AuthEndpoints` | `POST /api/auth/register`, `/login`, `/google`; `GET /api/auth/google/config`; `GET /api/auth/me`. Publik kecuali `/me`. |
| `HotspotEndpoints` | Hotspot (`GET` semua/satu: login; `POST/PUT/DELETE`: Admin), cuaca, prediksi (`GET`: login; `POST` buat baru: Admin), dan **simulasi** (`GET /api/hotspots/{id}/simulation?hours=N`, login, `hours` dibatasi 1–24, default 6; mengembalikan `SimulationDto`). |
| `AdminEndpoints` | `/api/users`, `/api/users/{id}/toggle-active`, `/api/activity`, `/api/sync`, `/api/integrations/status` (periksa key FIRMS dan OpenWeather sekarang juga). Semua Admin. Memuat `ActivityRequest`. |
| `Policies` | Nama kebijakan otorisasi (`Admin`). |
| `Mappings` | Mengubah entity menjadi DTO (`ToDto()`), termasuk `SimulationResult` → `SimulationDto`. |

#### `Services/` (logika use case)
| Class | Fungsi |
|---|---|
| `AuthService` | Registrasi, login email+password, login Google. Hasilnya `AuthResult` (user + token). Melempar `AuthException` (HTTP 401) bila gagal. |
| `TokenService` | Membuat token JWT (klaim `sub`, `email`, `name`, `role`). Memuat `JwtOptions`. |
| `Passwords` | Hash dan verifikasi password (PBKDF2 bawaan ASP.NET Identity). |
| `HotspotService` | Ambil (filter sumber/status), tambah manual, ubah, hapus, dan impor hotspot satelit dengan deduplikasi. |
| `PredictionService` | Ambil/simpan cuaca per hotspot (cuaca segar < 60 menit dipakai ulang, yang usang diambil ulang dari OpenWeather; bila gagal, yang tersimpan dipertahankan), hitung dan simpan prediksi. |
| `FwiPredictionModel` | Rumus prediksi (arah, kecepatan, luas, risiko) dari cuaca. |
| `FireSpreadSimulator` | **Cellular automata deterministik**: grid sel (±0,56 km), peluang menyala dari kekeringan, suhu, hujan, dan keselarasan dengan arah rambatan (kebalikan arah asal angin; angin tenang merata). `Run()` mengembalikan `SimulationResult` (kondisi, ukuran sel, frame kumulatif per jam, penanda `Truncated`); maksimum 400 sel dan 24 jam. Hasil sama untuk input sama. Detail: [docs/simulasi-api.md](docs/simulasi-api.md). |
| `UserService` | Daftar pengguna, aktif/nonaktifkan (admin aktif terakhir tidak boleh dinonaktifkan). |
| `SyncService` | Sinkronisasi: per sumber FIRMS ambil, urai, impor. Hanya gangguan sementara yang dicoba ulang (jeda berlipat dua); key salah atau batas terlampaui tidak diulang dan menghentikan sumber lain; kegagalan sebagian dilaporkan per sumber. Impor massal (satu query cek duplikat, Id sekaligus). Satu sync pada satu waktu. |
| `SyncBackgroundService` | Menjalankan `SyncService` berkala. Mati kecuali `Sync:Enabled=true`. |
| `ActivityLogService` | Catat dan baca log aktivitas ("Admin menambah hotspot ..."). |

#### `Domain/` (entity dan aturan bisnis, tanpa EF/HTTP)
| Class | Fungsi |
|---|---|
| `Hotspot` (abstrak) | Titik api. Setter privat, validasi di konstruktor/method. `IsEditable` dan `GetDetails()` abstrak (polimorfisme). |
| `SatelliteHotspot`, `ManualHotspot` | Turunan `Hotspot`. Satelit tidak bisa diubah/hapus; manual bisa (`UpdateReport`). |
| `User` (abstrak), `Admin`, `RegisteredUser` | Pengguna. `CanManageHotspots` polimorfik. Status aktif lewat `Activate()/Deactivate()`. |
| `WeatherData`, `Prediction` | Cuaca di sekitar hotspot dan hasil prediksi. |
| `MonitoringArea`, `Notification` | Belum dipakai endpoint (lihat B2, B3). |
| `DomainException`, `Guard` | Pelanggaran aturan bisnis (HTTP 400) dan pemeriksaan invarian bersama (`Guard` bersifat `internal`). |
| `Coordinate`, `Vector` | Value object. |

#### `Data/` (database)
| Class | Fungsi |
|---|---|
| `AppDbContext` | Pemetaan semua entity ke tabel PostgreSQL (snake_case, pewarisan tabel). `NextIdAsync` membuat Id (`HS-`, `WX-`, `PR-`, `U-`) dari sequence database; mode InMemory memakai penghitung proses. |
| `DatabaseInitializer` | Saat start: buat tabel dan sequence (`Storage:AutoCreate`, default true), isi data demo (`Storage:SeedDemoData`, default true untuk InMemory dan false untuk Postgres), buat admin pertama dari `Admin__*`. |
| `DemoData` | Data contoh hotspot, prediksi, dan pengguna. Password semua akun demo `Ignis@123`. |

#### `Integrations/` (panggilan ke luar)
| Class | Fungsi |
|---|---|
| `FirmsClient` | Ambil CSV hotspot NASA FIRMS per sumber (default tiga satelit VIIRS NRT, Indonesia, 1 hari). Mengklasifikasi error (400 `Invalid MAP_KEY`, 403, 429, 5xx), memastikan isi respons benar-benar CSV, dan menyamarkan key. `CheckConnectionAsync` memakai kotak kecil (1 transaksi). |
| `FirmsCsvParser` | Ubah CSV FIRMS menjadi `SatelliteHotspot` sesuai dokumentasi atribut: `acq_time` UTC → waktu lokal, `confidence` l/n/h → 30/60/90 (MODIS numerik dipakai apa adanya), kode satelit → nama, batas baris dengan prioritas keyakinan. Baris tidak valid dilewati dan dihitung. |
| `WeatherClient` | OpenWeather Current Weather (`units=metric`). `TryFetchAsync` mengembalikan null bila tanpa key atau gagal (alasan dicatat); `GetWeatherAsync` jatuh ke data simulasi. Error 401/429/5xx diklasifikasi; key disamarkan. |
| `GoogleOAuthClient` | Tukar kode Google menjadi identitas terverifikasi. Memuat `GoogleOptions`. |
| `IntegrationOptions` | Pengaturan FIRMS/OpenWeather (bagian `Integrations`): key, sumber, area, rentang hari, batas baris, umur cuaca, percobaan ulang. |
| `IntegrationException`, `IntegrationStatus` | Jenis kegagalan (`MissingKey`, `InvalidKey`, `RateLimited`, `Unavailable`, `BadResponse`) yang menentukan layak-tidaknya dicoba ulang; status bersama (penjaga satu-sinkronisasi dan alasan kegagalan cuaca terakhir). |

#### `Support/`
`DotEnv` (baca `.env` dari folder kerja ke atas), `ErrorHandling` (`DomainException` → 400, `AuthException` → 401, format `{ "error": "..." }`).

### 2.2 `Ignis.Frontend`

| Folder / class | Fungsi |
|---|---|
| `Services/Backend/IgnisApiClient` | Satu-satunya pintu ke server. Menyimpan sesi login (token + `CurrentUser`, `IsLoggedIn`, `IsAdmin`, event `SessionChanged`) dan melampirkan token ke tiap permintaan, termasuk `GetSimulationAsync`. Singleton. Pesan error dibungkus `BackendException`. |
| `Services/Backend/GoogleSignIn` | Alur login Google: buat PKCE, buka browser, tangkap kode di `127.0.0.1`. |
| `Services/Admin/AdminWorkspace` | Cache data (hotspot, prediksi, pengguna, aktivitas) sebagai baris tampilan, plus event perubahan. Dashboard utama juga mendengarkan event ini. |
| `Services/Admin/AppSettings` | Pengaturan lokal di `%AppData%\IgnisAdmin\settings.json` (alamat API, interval, key CARTO). |
| `ViewModels/Auth/*` | `LoginFormViewModel`, `RegisterViewModel`, `LoginWindowViewModel`: logika layar login/registrasi. |
| `ViewModels/MainWindowViewModel` (+ `.Simulation.cs`) | Dashboard peta utama: login/logout, menu profil, gerbang panel admin (`IsAdmin`), data dashboard dari API (angka risiko, hotspot terlihat, Recent Locations, status darurat). File `.Simulation.cs` memuat logika simulasi: memilih target, memuat dari API, memutar per jam, umur tiap sel, teks panel, dan data grafik. |
| `Views/Dashboard/*` | Peta utama (`MapCanvasPlaceholder`, Mapsui; sel simulasi digambar sebagai poligon menurut umur, panah arah rambatan, sorotan pin terpilih), `FireRiskCard` (angka risiko + pemutar simulasi, kiri-bawah), `SimulationPanel` (kompas, luas, grafik, legenda, kanan-bawah), sidebar dengan menu profil dan Logout, pencarian, dan `UserSettingsWindow`. |
| `Views/Admin/*` | Halaman admin: Dashboard, Kelola Hotspot, Semua Hotspot, Hotspot Manual, Riwayat, Pengguna, Pengaturan, form dan detail hotspot. `AdminMainWindow` menutup diri dan membuka login saat sesi berakhir. |
| `Views/Auth/*`, `Views/Shared/*` | Layar login/registrasi dan kontrol bersama. |
| `Models/Admin/Rows.cs` | Pembungkus DTO untuk tampilan (`HotspotRow`, `PredictionRow`, `UserRow`). |
| `Models/SimulationMapState`, `Models/Compass` | Keadaan simulasi untuk digambar di peta (sel + umur) dan nama arah mata angin. |
| `Navigation/DialogService` | Membuka jendela login, admin, pengaturan pengguna, dan kotak pesan. |
| `App.xaml.cs` | Daftar DI. `HttpClient` dibuat sekali di sini dengan alamat dari env `IGNIS_API_URL` atau `AppSettings.ApiBaseUrl`. |

### 2.3 `Ignis.Shared`
`Enums.cs` (`Role`, `HotspotSource`, `HotspotStatus`, `RiskLevel`, `ConnectionState`), `Dtos.cs` (semua DTO dan request/response, termasuk auth, `CoordinateDto`, `SimulationFrameDto`), `Region.cs` (wilayah untuk filter di desktop).

### 2.4 `Ignis.Tests`
`DomainTests` (konsep OOP, aturan entity), `ServiceTests` (service di atas EF InMemory), `ParserAndModelTests` (model prediksi, cuaca simulasi, `FireSpreadSimulatorTests`), `IntegrationTests` (parser CSV FIRMS, `FirmsClient`, `WeatherClient`, `SyncService`, kesegaran cuaca, endpoint status integrasi; semua dengan HTTP palsu), `SimulationTests` (`FireSpreadBehaviorTests`: arah angin, angin tenang, hujan, frame kumulatif, batas jam dan sel; `SimulationApiTests`: endpoint 401/404/200, `hours` dibatasi, hasil stabil), `AuthApiTests` (end-to-end: login, 401/403, registrasi, token rusak), `TestDb` (helper database tes). Jalankan: `dotnet test Ignis.Tests`.

### 2.5 Aturan akses endpoint (ringkas)

| Endpoint | Siapa |
|---|---|
| `/health`, `/api/auth/register`, `/login`, `/google`, `/google/config` | Publik |
| `GET /api/auth/me`, `GET /api/hotspots*` (termasuk `/simulation`, `/weather`, `/prediction`), `GET /api/predictions` | Pengguna login |
| `POST/PUT/DELETE /api/hotspots*`, `POST /api/hotspots/{id}/predictions`, `/api/users*`, `/api/activity`, `/api/sync`, `/api/integrations/status` | Admin |

---

## 3. Langkah mengerjakan dan integrasi

Aturan umum untuk semua fitur baru: **(1)** entity/aturan di `Domain/` → **(2)** logika di `Services/` → **(3)** route di `Endpoints/` → **(4)** DTO di `Ignis.Shared/Dtos.cs` → **(5)** method di `IgnisApiClient` → **(6)** tampilan/ViewModel → **(7)** tes di `Ignis.Tests`. Jalankan `dotnet build Ignis.slnx` dan `dotnet test Ignis.Tests` sebelum commit.

### Langkah 0 — Siapkan lingkungan (sekali)
1. Salin `.env.example` ke `.env`. Isi `Jwt__Key` (K1). Untuk mulai cepat biarkan `Storage__Provider=InMemory`.
2. `dotnet run --project Ignis.Api` lalu cek `http://localhost:5000/health`.
3. `dotnet run --project Ignis.Frontend`, login `admin@ignis.example` / `Ignis@123`.

### Langkah 1 — Paket rilis untuk penilaian (B11, K4) · P1
Tujuan: penilai menerima .exe desktop dan backend yang bisa langsung dijalankan, memakai PostgreSQL, dengan semua key diisi manual oleh Anda.

**1a. Pilih jalur PostgreSQL** (keputusan Anda):
- **Online (Neon/Supabase, gratis)**: paling mudah bagi penilai dan paling kecil. Buat satu database khusus tugas, ambil connection string, isi `ConnectionStrings__Ignis` di `.env`. Butuh internet. Pastikan database tidak "tidur" atau habis kuota sampai penilaian selesai.
- **PostgreSQL portable (offline)**: zip binary tanpa installer ditaruh di folder `postgres/`, dan `.bat` menjalankan `initdb` (sekali), `pg_ctl start`, `createdb ignis`, lalu API, serta `pg_ctl stop` saat ditutup. Pakai port 5433 agar tidak bentrok dengan PostgreSQL penilai. Paket menjadi ±500 MB.
- Docker tidak disarankan sebagai satu-satunya jalur (penilai belum tentu punya).

**1b. Uji jalur PostgreSQL dulu (K4)**:
1. Di `.env`: `Storage__Provider=Postgres`, `ConnectionStrings__Ignis=...`, `Storage__SeedDemoData=true`.
2. Jalankan API. Periksa tabel dan sequence terbentuk (`\dt`, `SELECT nextval('ignis_hotspot_seq')`).
3. Dari desktop: tambah hotspot manual, pastikan Id `HS-1000` dan seterusnya, dan baris ada di `hotspots` dan `manual_hotspots`.
4. Bila ada error di jalur Postgres (sequence, `EnsureCreated`, tipe waktu), perbaiki sebelum lanjut.

**1c. Publish**:
```bash
dotnet publish Ignis.Api -c Release -r win-x64 --self-contained -o Ignis-Release/Server
dotnet publish Ignis.Frontend -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o Ignis-Release/Aplikasi
```
WPF dengan paket OpenTK/SkiaSharp (peringatan NU1701) kadang butuh penyesuaian saat single-file; uji hasilnya. Siapkan ruang disk (drive C: sempat penuh; publish ke drive D:).

**1d. Susun paket**:
```
Ignis-Release/
├── Server/                Ignis.Api.exe (self-contained)
├── Aplikasi/              Ignis.exe
├── postgres/              (hanya jalur portable)
├── .env                   diisi Anda; berisi key sungguhan, jangan di-commit
├── jalankan-server.bat    atau jalankan-semua.bat untuk jalur portable
└── PETUNJUK.txt           langkah, akun login, dan cara melewati peringatan SmartScreen/firewall
```
Isi `PETUNJUK.txt`: 1) jalankan `.bat`, 2) buka `Ignis.exe`, 3) login. Sebutkan bahwa login Google butuh kunci Google yang tidak disertakan, kecuali Anda menyertakannya di `.env`.

**1e. Uji di komputer bersih** (akun Windows lain atau mesin virtual tanpa .NET): ekstrak, jalankan `.bat`, login, tambah hotspot, buka simulasi, cek tabel. Gunakan password database buangan dan key yang bisa dicabut setelah penilaian.

### Langkah 2 — Uji manual GUI dan rapikan (K8, F1, F4, F5, F8, F9, K10–K12) · P1–P2
Jalankan API lalu desktop, dan centang satu per satu:
- [ ] Belum login: dashboard menampilkan "Klik Profil untuk Login" dan tidak crash.
- [ ] Login admin: angka risiko, pin peta, dan Recent Locations terisi dan sama dengan isi tabel `hotspots`/`predictions`.
- [ ] Login user biasa (`andi.pratama@example.com`): dashboard terisi, tombol Admin tidak tampil.
- [ ] Tombol Admin (akun admin): panel admin terbuka; akun biasa mendapat pesan penolakan.
- [ ] Simulasi: klik pin hotspot (F10). Panel muncul, sel terbakar membesar di peta saat play, warna dari kuning ke merah tua, panah biru searah rambatan.
- [ ] Klik pin lain: hasil lama hilang dan berganti; klik **Lihat detail hotspot** membuka detail.
- [ ] Tutup panel (tombol ✕): sel dan panah hilang dari peta.
- [ ] Logout: dashboard dikosongkan.
- [ ] Token kedaluwarsa: set `Jwt:ExpiresMinutes=1`, buka panel admin, tunggu, pastikan login muncul lagi (F4).
- [ ] Matikan API lalu buka dashboard: saat ini kosong tanpa pesan. Perbaiki dengan pesan jelas (F9).
- [ ] Ubah alamat API di Pengaturan, tutup dan buka lagi aplikasi (F8), lalu tambahkan teks "mulai ulang aplikasi" di jendela itu.
- [ ] Hapus `Ignis.Tests/TmpAccessDemo.cs`, commit `docs/konsep-oop.md`, perbarui dua baris usang di README (K10–K12).

### Langkah 3 — Simulasi penyebaran api (B1, F2) · SELESAI
Sudah dikerjakan dan dijelaskan di [docs/simulasi-api.md](docs/simulasi-api.md): model, rumus, konvensi arah angin, bentuk respons, tampilan, dan daftar tes.
Pengembangan lanjutan (opsional, di luar lingkup tugas): tipe lahan/bahan bakar, medan (kemiringan), cuaca yang berubah tiap jam (prakiraan), dan pemilihan hotspot dari daftar Semua Hotspot di panel admin.

### Langkah 4 — Area pantauan dan notifikasi (B2, B3, F6) · P2
1. **Area pantauan**: buat `MonitoringAreaService` (tambah, daftar milik user, hapus). Validasi lintang/bujur di entity (ikuti pola `Guard` pada `Hotspot`). Id dari `NextIdAsync` (tambahkan `IdKind.Area` dan sequence-nya di `AppDbContext` dan `schema.sql`).
2. **Endpoint**: `/api/monitoring-areas` (GET, POST, DELETE), hanya pemilik. Ambil `userId` dari klaim `sub` di token, bukan dari body request.
3. **Notifikasi**: `NotificationService` dengan `CheckNewHotspotsAsync(userId, since)` (hotspot dengan `DetectedAt > since` di dalam `MonitoringArea.Contains`) dan penyimpanan `Notification`. Panggil setelah `SyncService` mengimpor hotspot baru.
4. **Endpoint**: `GET /api/notifications` dan `POST /api/notifications/{id}/read`. Mulai dengan polling dari frontend (timer per beberapa menit); SignalR bisa belakangan.
5. **Frontend**: layar kelola area, ikon lonceng dengan jumlah belum dibaca, dan ganti `SavedLocationsCount` agar memakai area/lokasi tersimpan yang sungguhan. Hormati `AppSettings.NotificationsEnabled`.
6. **Tes**: area mengandung titik, notifikasi hanya untuk pemilik area, user lain tidak bisa membaca/menghapus area orang lain (403/404).

### Langkah 5 — Migration dan skema (B4) · P2
1. Pasang `dotnet-ef` dan tambah paket `Microsoft.EntityFrameworkCore.Design` di `Ignis.Api`.
2. Buat migration awal dari model saat ini: `dotnet ef migrations add Awal --project Ignis.Api`. Pastikan hasilnya sama dengan `database/schema.sql`.
3. Ganti `EnsureCreatedAsync` di `DatabaseInitializer` dengan `MigrateAsync`. Pertahankan pembuatan sequence Id (masukkan ke migration atau biarkan `IF NOT EXISTS`).
4. Perhatikan: database yang sudah dibuat dengan `EnsureCreated` tidak punya tabel `__EFMigrationsHistory`. Untuk database dev, hapus dan buat ulang.
5. Setelah itu, setiap perubahan entity = satu migration baru. Lakukan ini setelah paket penilaian selesai, agar paket tidak berubah di menit terakhir.

### Langkah 6 — Pengaturan dan keamanan (B5–B8, F7) · P3
1. **Pengaturan (B5, F7)**: pilih satu. (a) Sediakan `GET/PUT /api/settings` (Admin) yang menyimpan key FIRMS/weather di database terenkripsi, lalu `SyncService` membaca dari sana; atau (b) hapus isian key dari `PengaturanPage` dan jelaskan bahwa key diisi di `.env` server. (b) lebih sederhana dan aman, dan cocok dengan paket penilaian.
2. **Role (B6)**: `PUT /api/users/{id}/role` (Admin). Aturan: admin aktif terakhir tidak boleh diturunkan (pola sama dengan `ToggleActiveAsync`). Mengubah role user EF dengan pewarisan tabel (TPT) tidak bisa lewat satu entity; hapus dan buat ulang baris di tabel anak, atau ganti desain role menjadi kolom biasa.
3. **Cabut token (B7)**: tambah klaim versi (`token_version`) di `User` dan token, lalu periksa di event `OnTokenValidated` JwtBearer. Naikkan versi saat user dinonaktifkan atau logout.
4. **Rate limiting (B8)**: pakai `AddRateLimiter` bawaan ASP.NET untuk `/api/auth/login` dan `/register`.
5. **HTTPS (B9)**: pasang di reverse proxy (Caddy/Nginx) atau Kestrel dengan sertifikat, lalu set `IGNIS_API_URL` di desktop ke `https://...`.

### Langkah 7 — Login Google (K2, K7) · setelah kunci siap
1. Ikuti [docs/google-oauth.md](docs/google-oauth.md): buat proyek, consent screen, tambah diri sebagai Test user, buat OAuth client **Desktop app**.
2. Isi `Google__ClientId` dan `Google__ClientSecret` di `.env`, restart API. Cek `GET /api/auth/google/config` mengembalikan Client ID.
3. Jalankan desktop, klik tombol Google, selesaikan di browser. Pastikan masuk sebagai `Registered`.
4. Bila gagal, lihat tabel "Masalah umum" di dokumen itu, dan laporkan pesan error aslinya.
5. Bila kunci Google akan ikut di paket penilaian, ingat penilai harus masuk sebagai Test user (atau Publish app), kalau tidak Google menolak.

### Urutan pengerjaan yang disarankan
`Langkah 1` (paket penilaian, termasuk uji PostgreSQL) → `2` (uji GUI dan rapikan) → `7` (Google, kapan pun kunci siap) → `4` (area dan notifikasi) → `5` → `6`. (Langkah 3, simulasi, sudah selesai.)
Bila waktu mepet: kerjakan 1 dan 2 dulu, karena itu yang dinilai langsung; 4–6 masuk daftar "pekerjaan lanjutan" di laporan.
