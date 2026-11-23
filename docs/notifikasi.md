# Lokasi tersimpan dan notifikasi dampak kebakaran

Pengguna menyimpan lokasi pentingnya (rumah, kantor, kebun). Bila simulasi memperkirakan **api** atau **asap** sebuah hotspot mendekati lokasi itu
dalam radius yang ditentukan pengguna, server membuat notifikasi, dan aplikasi desktop menampilkannya lewat lonceng, banner, dan peta.

> Semua angka dan peringatan berasal dari **simulasi ilustratif** (lihat [simulasi-api.md](simulasi-api.md)), bukan prakiraan resmi.
> Setiap notifikasi menyebutnya, dan tidak menggantikan peringatan dari BNPB/BMKG atau pihak berwenang.

## 1. Dari sisi pengguna

| Yang ingin dilakukan | Caranya |
|---|---|
| Menyimpan lokasi | Sidebar → **LOKASI SAYA** → **+ Tambah**. Isi nama, lalu ketik lintang/bujur **atau** klik **Pilih titik langsung di peta** dan klik lokasi Anda. Atur radius 1–50 km. |
| Melihat status | Tiap lokasi di sidebar menampilkan dampak simulasi hotspot yang sedang dipilih (mis. "HS-1218: api sudah dalam radius · asap pekat ±3 jam"). Di peta lokasi ditandai titik dengan cincin radius (hijau = aman, merah = ada notifikasi belum dibaca). |
| Mengetahui ada bahaya | **Lonceng** di bar atas menampilkan jumlah belum dibaca. Notifikasi baru juga muncul sebagai **banner** di atas peta (bisa dimatikan di Pengaturan). |
| Melihat detail | Klik lonceng → daftar notifikasi (terbaru di atas, berwarna menurut tingkat). Klik satu notifikasi: ditandai dibaca, peta memusatkan ke hotspot, dan simulasinya dijalankan. |
| Memeriksa sekarang | Lonceng → **Periksa sekarang** (server menyimulasikan ulang dengan cuaca terbaru). |
| Menghapus | Lokasi: **Hapus** (notifikasinya ikut terhapus). Notifikasi: tombol ✕. |

Tingkat peringatan:

| Tingkat | Api | Asap |
|---|---|---|
| **Bahaya** (merah) | api diperkirakan masuk radius dalam ≤ 3 jam, atau sudah di dalamnya | asap **pekat** masuk radius |
| **Waspada** (oranye) | ≤ 8 jam | asap **sedang** |
| **Perhatian** (kuning) | selebihnya dalam horizon | asap **ringan** |

Lokasi tersimpan dan notifikasi hanya untuk akun **pengguna terdaftar**. Akun **admin** tidak memilikinya (sesuai ERD: tabel lokasi dan notifikasi merujuk
pengguna terdaftar); sidebar admin menampilkan penjelasan dan lonceng disembunyikan. Admin tetap bisa memicu pemeriksaan untuk semua pengguna.

## 2. Kapan notifikasi dibuat

Server (`NotificationService`) memeriksa lokasi pengguna pada saat:

1. **Sinkronisasi hotspot** selesai (manual dari panel admin, atau terjadwal bila `Sync__Enabled=true`). Hotspot baru dan cuaca terbaru bisa mengubah dampaknya.
2. **Admin menambah atau mengubah hotspot manual** (hanya lokasi di dekat hotspot itu yang diperiksa).
3. **Pengguna menambah atau mengubah lokasi** (langsung diperiksa, jadi pengguna segera tahu bila lokasinya sudah terancam).
4. **Pengguna menekan "Periksa sekarang"**, atau admin memanggil `POST /api/notifications/evaluate` (semua lokasi).

> Tanpa sinkronisasi terjadwal, hotspot satelit baru **tidak** otomatis memicu notifikasi sampai ada yang menjalankan sinkronisasi. Untuk pemakaian sungguhan
> aktifkan `Sync__Enabled=true` (lihat [integrasi-data.md](integrasi-data.md)). Aplikasi desktop menanyakan server tiap 60 detik selama berjalan; notifikasi
> yang dibuat saat aplikasi tertutup tampil saat aplikasi dibuka lagi. Tidak ada pesan ke luar aplikasi (email/SMS/push).

### Alur untuk tiap lokasi

1. **Pilih hotspot kandidat**: status bukan Resolved; hotspot satelit tidak lebih tua dari 36 jam (hotspot manual yang belum ditutup selalu dipakai); berjarak ≤ 70 km
   dari lokasi. Hotspot yang berjarak < 5 km satu sama lain dianggap **satu gugus** kebakaran dan hanya yang terdekat disimulasikan; paling banyak 4 hotspot per lokasi.
   Ini menjaga beban: data nyata Indonesia bisa berisi ribuan deteksi, tetapi hanya segelintir yang disimulasikan per lokasi.
2. **Simulasikan** tiap kandidat 12 jam ke depan dengan cuaca terbaru (cuaca tersimpan dipakai bila masih segar, lihat integrasi-data).
3. **Nilai dampak** ([ExposureEvaluator](../Ignis.Api/Services/ExposureEvaluator.cs)): api/asap "mencapai" lokasi bila jarak dari lokasi ke **tepi** sel yang terbakar/berasap ≤ radius.
   Dicatat jam pertama mencapai radius (0 = sudah), jarak terdekat, dan tingkat asap tertinggi.
4. **Ambil kasus terburuk per jenis** (api, asap) dari semua kandidat: satu lokasi menerima paling banyak **dua notifikasi per putaran** (api dan asap), bukan satu per hotspot.
5. **Cegah pengiriman berulang**: notifikasi sejenis (lokasi + jenis) tidak dikirim lagi selama **6 jam**, kecuali tingkatnya **naik** (eskalasi). Setelah 6 jam dan keadaan masih sama, pengguna diingatkan lagi.
6. Hanya pengguna **aktif** yang diberi notifikasi.

## 3. Model asap

Asap disimulasikan bersama api ([SmokePlumeModel](../Ignis.Api/Services/SmokePlumeModel.cs)), pada grid kasar 0,025° (±2,8 km, lima kali sel api), sebagai
**adveksi–difusi** deterministik:

- Tiap jam, setiap sel api yang terbakar **memancarkan** asap ke sel kasar yang memuatnya.
- Asap **terbawa angin** ke arah rambatan (semi-Lagrangian, dibagi menjadi langkah kecil agar jejaknya menerus), **menyebar** (difusi 8 km²/jam), dan **meluruh**
  (umur dasar 14 jam; **hujan mempercepat luruhnya**: umur = 14 / (1 + 0,8 × curah hujan)).
- Angin tenang menghasilkan sebaran melingkar; angin kencang menghasilkan jejak memanjang searah angin. Domain ±61 km dari sumber; asap yang melewatinya dianggap keluar.
- Kepekatan dinyatakan sebagai indeks "massa per km²" (massa 1 = satu sel api terbakar selama satu jam). Ambang level: ringan ≥ 0,01, sedang ≥ 0,12, pekat ≥ 0,60.
  Ambang ditetapkan dari pengukuran pada skenario khas (angin kencang kering, sedang, lemah, tenang, hujan) dan dikunci oleh tes; angkanya **bukan µg/m³**
  dan bukan indeks kualitas udara.
- Perilaku yang teramati: asap muncul dalam sekitar 3 jam, memanjang searah angin (di timur 61 km pada jam 12 untuk angin barat 5 m/s), melingkar saat tenang,
  menipis saat hujan, dan makin pekat untuk kebakaran yang lebih besar. Satu sel api tunggal pada angin kencang hanya memberi jejak sempit yang cepat encer (sesuai fisika pluma).
- Batas: tanpa data lahan (gambut menghasilkan jauh lebih banyak asap), tanpa lapisan atmosfer/inversi, dan sumber hanya dari sel api yang disimulasikan.

## 4. API

Semua butuh token. Lokasi dan notifikasi hanya untuk pengguna terdaftar; id milik orang lain dijawab 404.

| Endpoint | Fungsi |
|---|---|
| `GET /api/monitoring-areas` | Lokasi saya |
| `POST /api/monitoring-areas` | Simpan lokasi (`name`, `latitude`, `longitude`, `radiusKm`); batas 10 per pengguna; langsung memeriksa dampaknya |
| `PUT /api/monitoring-areas/{id}` | Ubah lokasi milik sendiri; memeriksa ulang |
| `DELETE /api/monitoring-areas/{id}` | Hapus lokasi beserta notifikasinya |
| `GET /api/notifications?unreadOnly=&limit=` | Daftar notifikasi (terbaru di atas) + jumlah belum dibaca |
| `POST /api/notifications/{id}/read`, `/read-all` | Tandai dibaca |
| `DELETE /api/notifications/{id}` | Hapus notifikasi |
| `POST /api/notifications/check` | Periksa lokasi saya sekarang → `{areasChecked, hotspotsSimulated, notificationsCreated}` |
| `POST /api/notifications/evaluate` | **Admin**: periksa semua lokasi semua pengguna |
| `GET /api/hotspots/{id}/simulation` | Kini memuat `smokeCells` per frame dan `impacts` (dampak pada lokasi **pengguna yang melihat**; kosong untuk admin) |

## 5. Pengaturan server

Bagian `Notifications` di `appsettings.json` atau `Notifications__...` di `.env` (semuanya opsional):

| Opsi | Default | Arti |
|---|---|---|
| `HorizonHours` | 12 | Berapa jam ke depan simulasi diperiksa (maks 24) |
| `CooldownHours` | 6 | Jeda notifikasi sejenis |
| `CandidateRadiusKm` | 70 | Hotspot yang lebih jauh diabaikan |
| `MaxCandidatesPerArea` | 4 | Hotspot (gugus) yang disimulasikan per lokasi |
| `ClusterSeparationKm` | 5 | Jarak minimum antar-gugus |
| `SatelliteMaxAgeHours` | 36 | Usia maksimum hotspot satelit |
| `MaxAreasPerUser` | 10 | Batas lokasi tersimpan |

## 6. Data

- `monitoring_areas`: titik pusat (`latitude`, `longitude`), `radius` (km), dan kotak min/max yang dihitung dari keduanya (ERD tetap terpenuhi).
- `notifications`: `area_id`, `kind` (`Fire`/`Smoke`), `level` (1–3), `eta_hours`, `distance_km` (api: jarak terdekat tepi api; asap: jarak lokasi ke hotspot sumber), plus kolom lama.
- Id memakai sequence PostgreSQL: `AR-` (lokasi) dan `NT-` (notifikasi).
- Database PostgreSQL yang sudah ada: kolom dan indeks baru ditambahkan otomatis saat API start (`ALTER TABLE ... ADD COLUMN IF NOT EXISTS`); `database/schema.sql` untuk database baru.

## 7. Pengujian

`dotnet test Ignis.Tests --filter "SmokeModelTests|MonitoringAreaDomainTests|NotificationDomainTests|GeoTests|ExposureEvaluatorTests|NotificationServiceTests|NotificationApiTests"`

| Yang dibuktikan | Contoh |
|---|---|
| Asap | angin dari barat/timur/utara/selatan membawa asap ke arah sebaliknya; tenang = merata; angin kencang = lebih jauh; hujan mencuci; kebakaran besar = lebih pekat; deterministik; tetap di dalam domain dan di bawah batas ukuran |
| Entitas | validasi nama, koordinat, radius 1–50; ubah bersifat atomik; Id sekali; tingkat 1–3 |
| Evaluator | jam tiba api dan asap dihitung persis pada geometri buatan tangan; radius lebih besar tidak pernah lebih lambat; horizon membatasi; jarak haversine dan ke sel |
| Notifikasi | dekat dan searah angin diberi tahu, searah sebaliknya/jauh tidak; arah angin menentukan siapa; hanya pemilik; hotspot Resolved dan satelit lama diabaikan; gugus yang sama disimulasikan sekali; maksimal 4 kandidat; pengguna nonaktif dilewati; **tidak berulang dalam masa jeda, naik tingkat tetap dikirim, setelah masa jeda diingatkan lagi** |
| Hook | sinkronisasi hotspot dekat lokasi menghasilkan notifikasi; kegagalan pemeriksaan tidak menggagalkan sinkronisasi; admin menambah/memindahkan hotspot memicu pemeriksaan |
| API | 401 tanpa token, admin 403 untuk fitur pengguna dan pengguna 403 untuk evaluate-all; validasi 400; batas 10; pengguna tidak bisa mengubah/menghapus milik orang lain (404); simulasi memuat asap dan dampak hanya untuk pemiliknya |

### Verifikasi dengan data nyata

Dijalankan dengan aplikasi desktop terhadap FIRMS dan OpenWeather sungguhan (penyimpanan di memori, 1.010 hotspot nyata): sebuah lokasi uji 6 km searah angin dari hotspot
nyata HS-1218 (angin dari 148° 3,0 m/s, "hujan rintik-rintik") langsung menghasilkan **dua notifikasi**: *Bahaya: api dekat "Rumah (uji)"* (api terdekat 2,9 km) dan
*Waspada: asap sedang diperkirakan sampai* (±2 jam). Lonceng menampilkan 2, membuka notifikasi menandainya dibaca dan memusatkan peta, "Periksa sekarang" tidak mengulang
(masa jeda), akun admin tidak mendapat lonceng, dan logout mengosongkan semuanya.

## 8. Keterbatasan dan yang belum

- **Tidak ada push saat aplikasi tertutup** dan tidak ada email/SMS; notifikasi muncul saat aplikasi berjalan (polling 60 detik) atau dibuka lagi.
- **Notifikasi otomatis butuh sinkronisasi terjadwal** (`Sync__Enabled=true`); tanpa itu hanya muncul lewat pemicu pada bagian 2.
- Jalur **PostgreSQL asli** (sequence `AR-`/`NT-`, `ALTER TABLE ... IF NOT EXISTS`) belum dijalankan; semua tes dan verifikasi memakai penyimpanan di memori (lihat K4 di TODO).
- Klik langsung pada peta untuk memilih lokasi sudah tersambung (mode pilih titik, dialog, banner) tetapi klik peta Mapsui-nya belum diuji otomasi; jalur kode yang sama dengan memilih titik lewat ketikan koordinat sudah teruji.
- Penilaian tunggal per jenis per lokasi: bila dua kebakaran berbeda mengancam lokasi yang sama, yang terburuk yang dilaporkan.
- Zona waktu: jam pada notifikasi mengikuti zona server (lihat B10).
