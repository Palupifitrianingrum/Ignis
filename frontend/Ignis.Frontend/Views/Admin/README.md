# Ignis Admin Interface (WPF)

Dokumentasi implementasi modul **Dashboard Admin** yang telah diintegrasikan ke dalam struktur proyek `Ignis.Frontend`.

## Struktur Modul di dalam `Ignis.Frontend`
- **`Views/Admin/`**: Halaman-halaman admin (`AdminMainWindow`, `DashboardPage`, `KelolaHotspotPage`, `SemuaHotspotPage`, `HotspotManualPage`, `UsersPage`, `RiwayatPage`, `PengaturanPage`, dialog form & detail).
- **`Views/Admin/Controls/`**: Komponen kontrol admin (`InfoRow`, `StatCard`, `MapView`).
- **`Models/Admin/`**: Model data admin (`Hotspot`, `WeatherData`, `Prediction`, `UserAccount`, `AppSettings`, `Region`, `RiskLevel`, dll).
- **`Services/Admin/`**: `DataStore` (state & in-memory store) dan `SyncService` (sinkronisasi NASA FIRMS & OpenWeather).
- **`Themes/AdminStyles.xaml`**: Resource dictionary dan gaya UI tema Ignis Admin (telah digabung ke `App.xaml`).

## Fitur dan Halaman Admin yang Berfungsi
| Halaman / Fitur | Deskripsi |
|---|---|
| **Dashboard** | Statistik hotspot, peta persebaran Mapsui, status API (NASA FIRMS & Weather), tombol Sync Ulang, daftar hotspot dan riwayat prediksi terbaru. |
| **Kelola Hotspot** | Peta interaktif, tambah hotspot manual, filter wilayah/sumber/status, edit/hapus/detail hotspot. |
| **Semua Hotspot** | Daftar seluruh data hotspot dengan filter dan pagination. |
| **Hotspot Manual** | Daftar laporan hotspot manual dari lapangan, aksi CRUD hotspot manual. |
| **Detail Hotspot** | Menampilkan informasi lokasi, parameter cuaca, kalkulasi prediksi perambatan api, serta opsi hitung ulang prediksi. |
| **Tambah / Edit Hotspot** | Input manual dengan validasi form dan opsi klik peta untuk otomatis mengisi koordinat lat/lon. |
| **Manajemen Pengguna** | Daftar pengguna, filter peran/status, aksi ubah status aktif/non-aktif, log aktivitas pengguna. |
| **Riwayat Prediksi** | Filter risiko dan tanggal, detail hotspot terkait, serta fitur **Ekspor CSV**. |
| **Pengaturan** | Konfigurasi interval sync, endpoint API, API Key FIRMS / Weather / CARTO, kebijakan retry, dan notifikasi. Disimpan otomatis ke `%AppData%\IgnisAdmin\settings.json`. |

