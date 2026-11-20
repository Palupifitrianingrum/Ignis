# Simulasi penyebaran api

Simulasi memperkirakan ke mana dan seberapa cepat api dari satu hotspot merambat dalam 1 sampai 24 jam ke depan, berdasarkan cuaca di lokasi itu.
Seluruh perhitungan ada di server ([FireSpreadSimulator.cs](../Ignis.Api/Services/FireSpreadSimulator.cs)); desktop hanya menampilkan hasilnya.

> Hasilnya **ilustratif**, bukan prakiraan operasional. Model tidak memakai jenis lahan atau bahan bakar, medan, ataupun data historis.

## Cara kerja model (cellular automata)

1. Ruang dibagi menjadi grid sel persegi sisi 0,005° (±0,56 km) dengan titik hotspot sebagai sel awal.
2. Tiap jam, setiap sel yang bertetangga (8 arah) dengan sel terbakar menjadi kandidat. Kandidat menyala bila "undian"-nya tidak melebihi **peluang penyalaan**.
3. Undian bersifat **deterministik**: hash dari (lokasi hotspot, sel, jam). Input yang sama selalu memberi hasil yang sama, jadi hasil bisa diuji dan tidak berubah tiap dibuka.
4. Sel yang sudah terbakar tidak padam. Frame berisi himpunan sel terbakar yang kumulatif, jadi luas tidak pernah mengecil.

### Peluang penyalaan

`peluang = peluang dasar × laju × dorongan angin × penalti diagonal`, dibatasi 0,01 sampai 0,9.

| Faktor | Rumus ringkas | Efek |
|---|---|---|
| Peluang dasar | `(0,22 + 0,35·kekeringan + 0,18·panas) × redam hujan`, dibatasi 0,12–0,85 | Udara kering dan panas mempercepat; hujan memperlambat |
| Kekeringan | `(100 − kelembapan) / 100` | Kelembapan tinggi memperlambat |
| Panas | `(suhu − 20) / 25` | Suhu tinggi mempercepat |
| Redam hujan | `1 − 0,15 × curah hujan (mm)`, minimal 0,1 | Hujan lebat hampir menghentikan api |
| Laju (`SpreadRateScale`) | 0,30 | Kalibrasi agar api tidak memenuhi grid dalam beberapa jam |
| Dorongan angin | searah angin: `1 + 2,6 × kekuatan × keselarasan`; melawan angin: `1 − 0,9 × kekuatan × keselarasan` (minimal 0,2) | Api memanjang searah angin |
| Kekuatan angin | `kecepatan / 10` (m/s), 0 sampai 1,3 | **Angin tenang = rambatan merata ke segala arah** |
| Penalti diagonal | 0,78 untuk tetangga diagonal | Mengurangi bentuk berkotak |

Grid dibatasi **400 sel**. Bila tercapai, sel yang paling "pasti" menyala dipertahankan (urutan tetap), dan hasil memberi tanda `truncated` supaya tampilan bisa memberi tahu bahwa luas tidak bertambah lagi karena batas model, bukan karena api padam.

### Konvensi arah angin (penting)

`WeatherData.WindDirection` adalah arah **asal** angin (seperti OpenWeather): 270° berarti angin dari barat. Api didorong ke arah sebaliknya:

```
arah rambatan = (WindDirection + 180) mod 360      # angin dari barat (270) -> api ke timur (90)
```

Hasil menyertakan keduanya (`WindFromDirection` dan `SpreadDirection`). Sebelumnya simulator memakai `WindDirection` apa adanya sehingga api merambat ke arah asal angin; kesalahan itu sudah diperbaiki dan dikunci oleh tes.

### Satuan dan ukuran sel

- Kecepatan angin: m/s. Suhu: °C. Kelembapan: %. Curah hujan: mm.
- Luas per sel `= (0,005 × 111,32 km)² × cos(lintang)` dalam hektare. Di khatulistiwa ±31 ha, makin ke selatan/utara makin kecil.

## Endpoint

`GET /api/hotspots/{id}/simulation?hours=N` — butuh login (semua peran). Tanpa token 401, id tidak ada 404.

- `hours` dibatasi 1 sampai 24 (default 6).
- Memakai **cuaca terakhir yang tersimpan** untuk hotspot itu (diambil dulu bila belum ada), jadi dua panggilan berturut-turut memberi hasil identik.
- Tanpa API key OpenWeather, cuaca berasal dari penyedia simulasi dan `condition` bernilai `"Simulasi"`; desktop menampilkannya sebagai "data cuaca simulasi".

Bentuk respons (`SimulationDto` di [Dtos.cs](../Ignis.Shared/Dtos.cs)):

```json
{
  "hotspotId": "HS-0142",
  "originLatitude": 0.32, "originLongitude": 101.85,
  "hours": 18,
  "spreadDirection": 257, "windFromDirection": 77, "windSpeed": 4.9,
  "temperature": 29.7, "humidity": 55, "precipitation": 0.8, "condition": "Simulasi",
  "cellSizeDegrees": 0.005, "cellAreaHa": 30.98, "maxCells": 400, "truncated": false,
  "frames": [
    { "hourOffset": 0, "burningCells": [ { "latitude": 0.32, "longitude": 101.85 } ], "areaHa": 31, "newCells": 1 },
    { "hourOffset": 1, "burningCells": [ "..." ], "areaHa": 62, "newCells": 1 }
  ]
}
```

Tiap frame memuat semua sel terbakar pada jam itu (kumulatif); `newCells` adalah yang baru menyala pada jam itu. Ukuran respons paling besar sekitar 25 frame × 400 sel.

## Tampilan di desktop

Alur: klik pin hotspot di peta (atau item **Recent Locations** di sidebar) → `MainWindowViewModel.SelectSimulationTargetAsync` memanggil endpoint → simulasi diputar otomatis satu jam per 0,7 detik.

| Bagian | Isi |
|---|---|
| **Peta** ([MapCanvasPlaceholder.xaml.cs](../Ignis.Frontend/Views/Dashboard/MapCanvasPlaceholder.xaml.cs)) | Sel digambar sebagai **kotak** sesuai grid, diwarnai menurut umur: kuning (baru menyala), oranye, merah tua (lama terbakar). Panah biru menunjukkan arah rambatan, dan pin hotspot yang dipilih diberi cincin biru. Titik asal juga diberi cincin putih-merah, tetapi tertutup pin hotspot; cincin itu baru terlihat bila pin disembunyikan lewat filter. |
| **Panel simulasi** ([SimulationPanel.xaml](../Ignis.Frontend/Views/Dashboard/SimulationPanel.xaml)) | Kompas dengan panah arah rambatan, keterangan angin dan kondisi cuaca, luas terbakar dan jumlah sel pada jam itu, grafik luas per jam dengan penanda jam sekarang, legenda warna, catatan bila model mencapai batas sel, dan tombol **Lihat detail hotspot**. |
| **Pemutar** ([FireRiskCard.xaml](../Ignis.Frontend/Views/Dashboard/FireRiskCard.xaml)) | Tombol play/pause, slider per jam (dengan batas atas dari server), dan jam dinding perkiraan. Berhenti di jam terakhir; play lagi memulai dari awal. |

Perilaku penting:
- Memilih hotspot lain menghapus hasil sebelumnya terlebih dulu, jadi data hotspot lama tidak pernah tampil di bawah nama hotspot baru.
- Pilihan yang lebih baru membatalkan permintaan yang masih berjalan (hasil yang terlambat datang diabaikan).
- Hasil disimpan sementara per sesi; cache dibuang bila data hotspot berubah atau pengguna logout.
- Bila server tidak terjangkau, panel menampilkan pesan error dan aplikasi tidak crash.
- Setelah login, hotspot Active pertama dipilih sebagai simulasi awal (tanpa diputar dan tanpa menggeser peta).

## Pengujian

`dotnet test Ignis.Tests --filter "FireSpreadBehaviorTests|SimulationApiTests|FireSpreadSimulatorTests"`

| Yang dibuktikan | Tes |
|---|---|
| Angin dari barat/timur/utara/selatan mendorong api ke timur/barat/selatan/utara | `Fire_SpreadsAwayFromWhereWindComesFrom` |
| `SpreadDirection` = kebalikan arah asal angin | `Result_ReportsSpreadDirection_AsOppositeOfWindOrigin` |
| Angin tenang merata, angin kencang bergeser jauh | `CalmWind_SpreadsEvenly_StrongWindDrifts` |
| Hujan lebat dan kelembapan tinggi memperlambat | `HeavyRain_AndHumidity_SlowTheSpread` |
| Frame kumulatif, jam 0 hanya titik asal, `NewCells` dan luas konsisten | `Frames_AreCumulative_...` |
| Sel sejajar grid, `hours` dibatasi 1–24, batas 400 sel dilaporkan | `Cells_AlignToGrid_...`, `Hours_AreClamped`, `CellCount_NeverExceedsMax_...` |
| Hasil deterministik, luas sel mengecil menurut lintang | `SameInput_GivesIdenticalOutput_...`, `CellArea_ShrinksWithLatitude` |
| Endpoint: 401 tanpa token, 404 id tidak ada, user biasa boleh baca, payload lengkap, `hours` dibatasi, hasil identik antar-panggilan | `SimulationApiTests` |

Tampilan sudah diperiksa dengan menjalankan aplikasi sungguhan terhadap API sungguhan dan mengambil screenshot jendela pada jam 0, 9, dan 18, serta pada kondisi server mati.
Interaksi klik langsung pada pin peta belum diuji otomasi (pilihan hotspot diuji lewat daftar Recent Locations yang memakai jalur kode sama, `SelectSimulationTargetAsync`).
