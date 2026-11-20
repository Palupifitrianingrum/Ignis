# Integrasi data: NASA FIRMS dan OpenWeather

Server (`Ignis.Api`) mengambil dua jenis data dari luar: **hotspot satelit** dari NASA FIRMS dan **cuaca** dari OpenWeather.
Desktop tidak pernah memegang key; semuanya diisi di server lewat file `.env`.

| Layanan | Dipakai untuk | Tanpa key |
|---|---|---|
| NASA FIRMS | Hotspot satelit (VIIRS) untuk Indonesia, diimpor ke database | Sinkronisasi hotspot dilewati; data demo/manual tetap jalan |
| OpenWeather | Cuaca di lokasi hotspot untuk simulasi dan prediksi | Cuaca simulasi (ditandai "Simulasi"); aplikasi tetap jalan |

> Semua fakta tentang kedua layanan di bawah dibaca dari dokumentasi resminya (tautan di bagian [Sumber](#sumber)) dan, bila disebut "teramati",
> dicoba langsung terhadap server mereka. Pada 2026-11-21 pengambilan data sudah diverifikasi dengan **key sungguhan** (hasilnya di
> [Hasil verifikasi](#hasil-verifikasi-dengan-key-sungguhan-2026-11-21)); yang masih belum terbukti ada di [bagian terakhir](#yang-belum-terbukti).

---

## 1. Mendapatkan key

### 1.1 NASA FIRMS (MAP_KEY) — gratis

1. Buka https://firms.modaps.eosdis.nasa.gov/api/map_key/
2. Klik **Get MAP_KEY**.
3. Isi **alamat email** Anda (satu-satunya isian yang disebut dokumentasi), lalu klik **Get MAP Key**.
4. Key dikirim ke email itu. Dokumentasi tidak menyebut berapa lama pengirimannya; cek juga folder spam.

Hal yang perlu diketahui (dari dokumentasi FIRMS):
- Bila email sudah pernah terdaftar, FIRMS tidak memberi key baru lewat formulir. Untuk mengirim ulang, menghapus, atau mengubah key, login di halaman https://firms.modaps.eosdis.nasa.gov/download/.
- Batas: **5.000 transaksi per 10 menit** per key. Permintaan besar bisa dihitung beberapa transaksi. Sinkronisasi Ignis memakai satu permintaan per satelit, jadi jauh di bawah batas.
- Halaman key punya tombol **Check status** untuk melihat sisa transaksi.
- Format dan panjang key tidak dijelaskan dokumentasi; salin apa adanya tanpa spasi.

### 1.2 OpenWeather (API key) — gratis

1. Daftar di https://home.openweathermap.org/users/sign_up dan konfirmasi email.
2. Key (disebut `APPID`) dikirim lewat email, dan selalu bisa dilihat di tab **API keys**: https://home.openweathermap.org/api_keys
3. Anda bisa membuat key tambahan di halaman yang sama.

Hal yang perlu diketahui (dari FAQ dan dokumentasi OpenWeather):
- **Key baru aktif otomatis, bisa sampai 2 jam setelah mendaftar.** Selama belum aktif, server OpenWeather menjawab **HTTP 401**. Itu bukan berarti key salah; tunggu lalu coba lagi.
- Paket gratis: **60 panggilan per menit** (lebih dari itu HTTP 429). Batas berlaku per akun, semua key digabung.
- OpenWeather menyarankan **tidak memanggil lebih sering dari sekali per 10 menit untuk satu lokasi**. Ignis mematuhinya: cuaca tersimpan dipakai ulang selama 60 menit (`WeatherMaxAgeMinutes`).
- Ignis hanya memakai **Current Weather** (`data/2.5/weather`). Fitur berbayar seperti prakiraan 16 hari, data historis, dan One Call tidak diperlukan.
- Halaman FAQ yang dibaca tidak mencantumkan daftar API yang termasuk paket gratis. Bila key Anda sudah lewat 2 jam tetapi tetap 401, periksa paket di akun Anda.

---

## 2. Memasang key

Edit file **`.env`** di root repo (sudah di-`.gitignore`; salin dari `.env.example` bila belum ada). Jangan menaruh key di `appsettings.json`, di Git, atau di chat.

```
Integrations__FirmsKey=ISI_MAP_KEY_FIRMS_DI_SINI
Integrations__WeatherKey=ISI_API_KEY_OPENWEATHER_DI_SINI
```

Tanpa spasi di sekitar `=` dan tanpa tanda kutip. Lalu **restart `Ignis.Api`** (key dibaca saat start).
Alternatif untuk OpenWeather: variabel `OPENWEATHER_API_KEY` (dipakai bila `Integrations__WeatherKey` kosong).

### Memeriksa bahwa key bekerja

Pilih salah satu:

- **Dari desktop**: login sebagai admin, buka panel admin lalu jalankan **Sinkronisasi**. Pesan hasilnya per sumber menjelaskan apa yang terjadi (lihat tabel pesan di bawah).
- **Lewat API** (admin): `GET /api/integrations/status` memeriksa kedua key sekarang juga dengan satu permintaan kecil masing-masing. Contoh PowerShell:

```powershell
$tok = (Invoke-RestMethod -Method Post http://localhost:5000/api/auth/login -ContentType "application/json" `
        -Body '{"email":"admin@ignis.example","password":"Ignis@123"}').token
Invoke-RestMethod http://localhost:5000/api/integrations/status -Headers @{ Authorization = "Bearer $tok" } | ConvertTo-Json
```

Hasil yang diharapkan bila kedua key valid: `state: "Connected"` untuk keduanya.

Pemeriksaan FIRMS memakai kotak kecil (±22 km di Riau, 1 hari) sehingga hanya memakai 1 transaksi dan hampir tanpa data.

### Mengganti key dari aplikasi (admin)

Admin bisa mengganti key tanpa menyentuh file `.env`: buka panel admin, **Pengaturan**, kartu **API Key Layanan Data**. Aturannya:

| Hal | Perilaku |
|---|---|
| Prioritas key yang berlaku | **override dari aplikasi > key di `.env` > kosong** |
| Mengisi key baru | Menggantikan key yang berlaku. Key `.env` **tidak diubah atau dihapus**; tetap jadi default. |
| Kolom dibiarkan kosong | Tidak mengubah apa pun (tombol **Simpan key** nonaktif) |
| **Kembalikan ke default** | Membuang override sehingga key `.env` berlaku lagi (tombol hanya muncul bila ada override) |
| **Uji koneksi** | Menguji key yang diketik, atau key yang sedang berlaku bila kolom kosong, tanpa menyimpan |
| Saat **Simpan key** | Server menguji key dulu; hanya disimpan bila valid. Key yang gagal tidak pernah menimpa key yang sedang jalan. |
| Berlaku kapan | Seketika, tanpa restart server |
| Siapa | Hanya admin (pengguna biasa mendapat 403). Key milik server dan dipakai semua pengguna. |

Keamanan:
- Key disimpan **terenkripsi** (ASP.NET Data Protection) di tabel `app_settings`, tidak pernah dalam teks biasa.
- Key lengkap **tidak pernah dikirim balik** ke aplikasi; yang tampil hanya versi bertopeng, mis. `••••a3f9`.
- Penggantian dan pengembalian dicatat di log aktivitas (siapa dan kapan, **tanpa nilai key**).
- Isian key lokal yang dulu ada di halaman Pengaturan (tersimpan sebagai teks biasa di `%AppData%\IgnisAdmin\settings.json`) sudah dihapus, dan aplikasi membuang key lama dari file itu saat pertama dijalankan.

Yang perlu diketahui operator server:
- **Kunci enkripsi**: Windows menyimpannya di profil pengguna sehingga bertahan antar-restart. Di Docker/Linux set `DataProtection__KeysPath` ke folder yang disimpan (volume); bila tidak, override tidak bisa dibaca lagi setelah container diganti. Bila itu terjadi, server tetap start dengan key `.env` dan halaman Pengaturan menampilkan peringatan; isi ulang key atau klik **Kembalikan ke default**.
- Dengan penyimpanan **InMemory**, override hilang saat server berhenti (ikut data lainnya). Dengan PostgreSQL override bertahan.
- Tabel `app_settings` dibuat otomatis (juga untuk database PostgreSQL yang sudah ada).

API: `GET /api/settings/integrations`, `PUT|DELETE /api/settings/integrations/{firms|weather}`, `POST /api/settings/integrations/{firms|weather}/test` (semua admin).

---

## 3. Cara kerjanya

### 3.1 Hotspot dari FIRMS

Permintaan mengikuti dokumentasi Area API: `{endpoint}/{MAP_KEY}/{SOURCE}/{barat,selatan,timur,utara}/{DAY_RANGE}` → CSV.

- **Sumber** (default, urut): `VIIRS_NOAA20_NRT`, `VIIRS_NOAA21_NRT`, `VIIRS_SNPP_NRT` (tiga satelit VIIRS, near-real-time; FIRMS menyatakan data NRT tersedia dalam ±3 jam setelah pengamatan). Satu permintaan per sumber. **Suomi NPP diletakkan terakhir**: pada 2026-11-21 `data_availability` FIRMS melaporkan data NRT-nya berhenti di 2026-11-02, sedangkan NOAA-20 dan NOAA-21 sampai hari ini.
- **Area**: kotak Indonesia `94,-11,141,6`. **Rentang: 2 hari.** Temuan penting: tanpa parameter tanggal, `DAY_RANGE=1` hanya mencakup **tanggal UTC hari ini**, bukan 24 jam terakhir. Teramati untuk NOAA-20 di Indonesia: 1 hari = 0 baris, 5 hari = 2.382 baris. Karena itu default 2 hari (kemarin + hari ini, UTC) agar selalu mencakup 24 jam terakhir; duplikat dibuang saat impor.
- Hasil disaring dan diimpor; yang sudah ada (waktu scan dan koordinat sama) dilewati, jadi sinkronisasi berulang aman.
- Bila sebuah sumber mengembalikan 0 baris, Ignis menanyakan `data_availability` (1 transaksi tambahan) dan menuliskan tanggal data terakhir yang tersedia, sehingga "satelit sedang tidak mengirim data" bisa dibedakan dari "belum ada deteksi".
- Bila baris valid melebihi `FirmsMaxRows` (3000 per sumber), yang **keyakinan dan kecerahannya tertinggi** dipertahankan dan pesan hasil memberi tahu.

Pembacaan kolom CSV (berdasarkan dokumentasi atribut FIRMS) dan keputusan yang kami ambil:

| Kolom FIRMS | Perlakuan |
|---|---|
| `acq_date`, `acq_time` | **UTC** (HHMM, tanpa nol di depan: teramati `357` = 03:57). Diubah ke waktu lokal server karena seluruh aplikasi menyimpan waktu lokal. Server yang berjalan di zona UTC akan menyimpan UTC apa adanya (lihat B10 di TODO). |
| `confidence` | VIIRS: kategori `l`/`n`/`h` (low/nominal/high), **bukan angka**. Dipetakan ke skala aplikasi 0–100: **l=30, n=60, h=90** (konvensi kami; NASA tidak memberi persentase). MODIS: angka 0–100 dipakai apa adanya. Tidak dikenal: 60. |
| `bright_ti4` | Kecerahan (Kelvin), disimpan sebagai `Brightness`. MODIS: kolom `brightness`. |
| `satellite` | Kode diterjemahkan: `N` Suomi NPP, `N20` NOAA-20, `N21` NOAA-21, `A` Aqua, `T` Terra. |
| `instrument` | **Ada** di CSV Area API (teramati; berisi `VIIRS`), tetapi tidak di file publik `data_active_fire`. Bila kolomnya tidak ada, diisi "VIIRS" (atau "MODIS") menurut kolom kecerahan. |
| `frp`, `scan`, `track`, `daynight`, `version`, `bright_ti5` | Belum dipakai. |

Baris yang rusak atau melanggar aturan domain (mis. lintang 95) dilewati dan dihitung di pesan hasil.

### 3.2 Cuaca dari OpenWeather

- Dipanggil saat simulasi atau prediksi butuh cuaca sebuah hotspot, bukan untuk semua hotspot sekaligus.
- Cuaca tersimpan per hotspot. Selama **lebih muda dari 60 menit** dipakai ulang (hasil simulasi stabil); setelah itu diambil ulang dan disimpan sebagai baris baru.
- Satuan (`units=metric`): suhu °C, kelembapan %, tekanan hPa, kecepatan angin **m/s**, hujan `rain.1h` mm/jam.
- `wind.deg` adalah arah **asal** angin (konvensi meteorologi, dikonfirmasi FAQ OpenWeather); simulasi dan prediksi sudah memperhitungkannya.
- Bila pengambilan gagal: cuaca tersimpan terakhir dipakai (tidak ditimpa data simulasi). Bila belum ada sama sekali, data simulasi disimpan sebagai awal dan ditandai `Condition = "Simulasi"`. Alasan kegagalan dicatat di log server.

### 3.3 Penanganan kegagalan

| Penyebab | Contoh | Diulang? |
|---|---|---|
| Key salah / belum aktif / ditolak | FIRMS HTTP 400 `Invalid MAP_KEY.`, 401, 403; OpenWeather HTTP 401 | **Tidak** (tidak akan membaik); sumber lain ikut dilewati |
| Batas panggilan | HTTP 429 | **Tidak** |
| Gangguan sementara | Jaringan putus, timeout, HTTP 5xx | **Ya**: `RetryCount` kali (default 2), jeda `RetryDelaySeconds` (default 3 detik) berlipat dua |
| Jawaban tidak sesuai dokumentasi | HTTP 200 berisi HTML, bukan CSV | Tidak; dilaporkan sebagai error, bukan "0 hotspot baru" |

Satu sumber gagal tidak membatalkan sumber lain (kecuali gangguan koneksi pada sumber pertama). Hasil sebagian dilaporkan per sumber.

Pesan yang akan Anda lihat:

| Pesan | Arti dan tindakan |
|---|---|
| `Key FIRMS ditolak (Invalid MAP_KEY)` | Key FIRMS salah atau terpotong. Salin ulang dari email. |
| `FIRMS menolak permintaan (HTTP 403)` | Key salah, atau batas 5.000 transaksi/10 menit terlampaui. Tunggu 10 menit, lalu cek **Check status** di halaman key. |
| `API key OpenWeather ditolak (HTTP 401)` | Key salah atau **belum aktif (tunggu sampai 2 jam)**. |
| `Batas panggilan OpenWeather terlampaui (HTTP 429)` | Lebih dari 60 panggilan per menit. |
| `Respons FIRMS bukan CSV yang dikenali` | FIRMS menjawab sesuatu yang bukan data; isi cuplikannya ditampilkan (tanpa key). |
| `FIRMS … : N baris, M hotspot baru` | Berhasil. `N` baris di CSV, `M` yang belum ada di database. |
| `… 0 baris … (data terakhir yang tersedia di FIRMS untuk sumber ini: TANGGAL)` | Sumber itu tidak mengirim data baru. Bila tanggalnya sudah lama, satelit/sumber itu sedang bermasalah di FIRMS; bukan kesalahan Ignis atau key Anda. |

---

## 4. Keamanan key

- Key hanya di `.env` (di-`.gitignore`) atau variabel lingkungan server. Tidak pernah dikirim ke desktop.
- **Key FIRMS berada di path URL** (itu cara API-nya bekerja), jadi URL lengkap tidak boleh dicatat. Ditemukan saat pengujian: log bawaan `HttpClient` mencetak URL itu apa adanya. Sudah diperbaiki dengan menaikkan kategori log `System.Net.Http.HttpClient` ke `Warning` di `appsettings.json`, dan diverifikasi: tidak ada baris log yang memuat key. (Key OpenWeather ada di query string, yang disamarkan otomatis oleh .NET.)
- Semua pesan error yang ditampilkan atau dicatat disaring dari key (diganti `***`) dan ada tesnya.
- Bila key bocor: FIRMS lewat halaman /download; OpenWeather hapus dan buat key baru di tab API keys.

---

## 5. Pengaturan

Semua opsional; default sudah wajar. Ditulis sebagai `Integrations__NamaOpsi` di `.env` atau di bagian `Integrations` pada `appsettings.json`.

| Opsi | Default | Arti |
|---|---|---|
| `FirmsKey` | kosong | MAP_KEY FIRMS |
| `FirmsSources` | `VIIRS_NOAA20_NRT,VIIRS_NOAA21_NRT,VIIRS_SNPP_NRT` | Sumber dipisah koma. Nilai lain yang valid menurut dokumentasi: `MODIS_NRT`, `VIIRS_*_SP`, dll. |
| `FirmsArea` | `94,-11,141,6` | Kotak `barat,selatan,timur,utara` dalam derajat |
| `FirmsDayRange` | `2` | Hari per permintaan, dibatasi 1–5. **1 hari = tanggal UTC hari ini saja** (bisa nyaris kosong setelah 07:00 WIB). |
| `FirmsMaxRows` | `3000` | Batas baris per sumber per sinkronisasi |
| `WeatherKey` | kosong | API key OpenWeather (atau env `OPENWEATHER_API_KEY`) |
| `WeatherMaxAgeMinutes` | `60` | Umur maksimum cuaca tersimpan sebelum diambil ulang |
| `RetryCount` / `RetryDelaySeconds` | `2` / `3` | Percobaan ulang untuk gangguan sementara saja |
| `Sync__Enabled` / `Sync__IntervalHours` | `false` / `1` | Sinkronisasi hotspot otomatis berkala |

---

## 6. Pengujian

`dotnet test Ignis.Tests --filter "FirmsCsvParserTests|FirmsClientTests|WeatherClientTests|SyncServiceTests|WeatherFreshnessTests|IntegrationStatusApiTests|IntegrationKeyStoreTests|IntegrationKeyApiTests"`

Semua tes memakai HTTP palsu (tanpa key dan tanpa internet; fixture parser memuat baris FIRMS nyata yang teramati) dan membuktikan antara lain: urutan path URL sesuai dokumentasi, `acq_time` UTC bergeser tepat sebesar offset zona lokal, kategori confidence, error 400/401/403/429/5xx diklasifikasi benar, key salah tidak diulang, gangguan sementara diulang lalu berhasil, HTTP 200 berisi HTML dianggap error, key tidak pernah muncul di pesan, impor idempoten, 3000 baris diimpor cepat, cuaca segar dipakai ulang dan yang usang diambil ulang, endpoint status hanya untuk admin, serta override key: prioritas override > `.env`, terenkripsi di database, tidak pernah dikembalikan, diuji sebelum disimpan, key gagal tidak menimpa key yang jalan, bertahan setelah restart, dan tidak bisa dibaca bila kunci enkripsi berubah.

Dengan **key sungguhan** (2026-11-21), lihat bagian berikutnya. Terhadap server sungguhan dengan **key palsu** (tanpa data pribadi): FIRMS menjawab 400 `Invalid MAP_KEY.` dan OpenWeather 401; Ignis menampilkan pesan yang jelas, tidak mengulang, simulasi tetap berjalan dengan cuaca simulasi, dan log bersih dari key.

## Hasil verifikasi dengan key sungguhan (2026-11-21)

Dijalankan terhadap FIRMS dan OpenWeather sungguhan (penyimpanan di memori, jadi tidak menyentuh database):

| Pemeriksaan | Hasil |
|---|---|
| `GET /api/integrations/status` | FIRMS dan OpenWeather: `Connected` (key valid) |
| Sinkronisasi pertama | NOAA-20: 449 baris, NOAA-21: 561 baris, **1.010 hotspot baru** dalam 4,7 detik; Suomi NPP: 0 baris dengan penjelasan "data terakhir: 2026-11-02" |
| Sinkronisasi kedua | 0 hotspot baru (idempoten) |
| Jam deteksi | 2026-11-20 10:44 s/d 2026-11-21 02:08 (waktu lokal WIB, UTC+7); saat diperiksa pukul 03:14 yang terbaru berumur 1,1 jam, sesuai latensi NRT; **tidak ada yang di masa depan** (jika UTC dianggap lokal, deteksi akan tampak hingga 7 jam ke depan) |
| Confidence | 30 (low): 40, 60 (nominal): 965, 90 (high): 5 |
| Satelit | "NOAA-20 · VIIRS": 449, "NOAA-21 · VIIRS": 561 |
| Wilayah | semua di dalam kotak Indonesia; 1.010 Id unik |
| Cuaca OpenWeather | suhu 29,7 °C, RH 66%, angin 2,45 m/s dari 157°, hujan 0,1 mm, kondisi "hujan rintik-rintik" (bahasa Indonesia dari `lang=id`) |
| Simulasi dengan cuaca nyata | `condition` bukan "Simulasi"; angin dari 157° → api merambat ke 337° |
| Log server | 0 baris memuat key |
| Override key dari aplikasi desktop (halaman Pengaturan) | Key salah ditolak server (`Invalid MAP_KEY`) tanpa menimpa key yang jalan; key sungguhan lolos uji dan tersimpan sebagai override; "Kembalikan ke default" kembali ke `.env`; 0 peringatan binding |

## Yang belum terbukti

- **Jalur PostgreSQL** untuk impor besar (Id massal lewat sequence) belum dijalankan terhadap PostgreSQL sungguhan; tes dan verifikasi di atas memakai penyimpanan di memori.
- **Zona waktu**: konversi memakai zona server. Pengujian dilakukan di zona WIB (UTC+7). Server yang berjalan di zona UTC akan menyimpan UTC apa adanya.
- **Pengambilan terjadwal** (`Sync__Enabled=true`) belum dijalankan berjam-jam; jalurnya sama dengan sinkronisasi manual yang sudah teruji.
- **Ukuran data musim kebakaran**: pengamatan ini di musim hujan (±500 deteksi per satelit per 2 hari). Pada musim kemarau jumlahnya bisa jauh lebih besar; `FirmsMaxRows` (3000 per sumber) membatasi, dan yang diprioritaskan adalah keyakinan dan kecerahan tertinggi.
- **Paket gratis OpenWeather**: daftar API yang termasuk paket gratis tidak tercantum di halaman FAQ yang dibaca; namun Current Weather terbukti berfungsi dengan key yang dipakai.

## Sumber

- FIRMS Area API: https://firms.modaps.eosdis.nasa.gov/api/area/
- FIRMS MAP_KEY: https://firms.modaps.eosdis.nasa.gov/api/map_key/
- FIRMS FAQ (latensi NRT, kelas confidence): https://www.earthdata.nasa.gov/data/tools/firms/faq
- FIRMS atribut data aktif api (MODIS/VIIRS): https://www.earthdata.nasa.gov/data/tools/firms/active-fire-data-attributes-modis-viirs
- OpenWeather Current Weather: https://openweathermap.org/current
- OpenWeather API key: https://openweathermap.org/appid
- OpenWeather FAQ (aktivasi key, 401/429, arah angin): https://openweathermap.org/faq
