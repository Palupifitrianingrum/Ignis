# Ignis Admin (WPF, .NET 8)

## Menjalankan
1. Windows 10/11 dan **.NET 8 SDK** (Visual Studio 2022 versi 17.8 atau lebih baru). Tidak perlu WebView2 lagi.
2. Buka `IgnisAdmin.sln` di Visual Studio 2022 lalu tekan F5, atau jalankan `dotnet restore` + `dotnet run` di folder ini. Restore pertama mengunduh paket NuGet **Mapsui.Wpf** (butuh internet).
3. Peta memakai **Mapsui** (native WPF/SkiaSharp) dengan tile **Esri World Street Map** (sama seperti Ignis.Frontend, tanpa API key). Jika API key CARTO (carto.com/basemaps/apikey) diisi di Pengaturan > "API Key Peta CARTO" atau env var `CARTO_API_KEY`, peta otomatis memakai **CARTO Voyager**. Butuh internet saat peta tampil; buka ulang halaman peta setelah mengubah key.
4. Bila terjadi error saat berjalan, pesannya muncul di dialog dan dicatat di `%AppData%\IgnisAdmin\error.log`.

## Halaman (sidebar) dan tombol yang berfungsi
| Halaman | Fungsi |
|---|---|
| Dashboard | statistik, peta, status API, **Sync Ulang**, dobel-klik baris / klik titik peta = Detail |
| Kelola Hotspot | **+ Tambah**, Detail/Edit/Hapus, filter daerah/sumber/status (**Apply/Clear**) |
| Semua Hotspot | filter + paging (Sebelumnya/Berikutnya), Detail |
| Hotspot Manual | Tambah, Detail, Edit, Hapus |
| Detail Hotspot (dialog) | cuaca, prediksi, **Hitung Ulang Prediksi**, Edit, Hapus, Tutup |
| Tambah/Edit Hotspot (dialog) | validasi input, klik peta untuk isi koordinat, Simpan/Batal |
| Manajemen Pengguna | filter, Detail, Ubah Status (aktif/non-aktif) |
| Riwayat Prediksi | filter risiko & tanggal, Lihat Hotspot, **Ekspor CSV** |
| Pengaturan | interval sync, endpoint & API key, retry, notifikasi (disimpan ke `%AppData%\IgnisAdmin\settings.json`) |

## Catatan penting
- Data hotspot/pengguna/prediksi masih **in-memory** (`Services/DataStore.cs`). Ganti dengan database/backend sesuai ERD.
- **Sync FIRMS** mengimpor CSV area Indonesia (VIIRS_SNPP_NRT, 1 hari) bila API key diisi. Belum diuji dengan key asli.
- **Cuaca & prediksi** masih disimulasikan (`GetWeather`, `Predict`); Sync hanya mengecek koneksi Weather API. Ganti dengan data API dan model prediksi sebenarnya.
- Filter "Daerah" memakai bounding box perkiraan, bukan batas provinsi yang presisi.
- Hotspot satelit hanya bisa dilihat; hanya hotspot manual yang bisa di-edit/hapus.
- API key disimpan sebagai teks biasa di settings.json. Untuk produksi, pakai Windows Credential Manager atau DPAPI.
- Menu "Manajemen Pengaturan" di wireframe digabung ke "Pengaturan".
