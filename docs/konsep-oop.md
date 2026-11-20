# Penerapan Inheritance, Polymorphism, dan Encapsulation

Semua konsep ada pada satu fungsionalitas: **kelola hotspot** (tambah, ubah, hapus hotspot manual, dan lihat hotspot satelit).
Rantai kodenya: `HotspotFormWindow (WPF)` → `IgnisApiClient` → `HotspotEndpoints` → `HotspotService` → entity `Hotspot` → `AppDbContext` → PostgreSQL.
Alur lengkap dan diagram kelas ada di [oop-feature.md](oop-feature.md).

| Konsep | Class utama | File |
|---|---|---|
| Inheritance | `Hotspot` → `SatelliteHotspot`, `ManualHotspot` | [Hotspot.cs](../Ignis.Api/Domain/Hotspot.cs) |
| Polymorphism | `IsEditable`, `GetDetails()` | [Hotspot.cs](../Ignis.Api/Domain/Hotspot.cs), [HotspotService.cs](../Ignis.Api/Services/HotspotService.cs) |
| Encapsulation | properti `private set`, `Guard`, `AssignId`, `UpdateReport` | [Hotspot.cs](../Ignis.Api/Domain/Hotspot.cs), [DomainException.cs](../Ignis.Api/Domain/DomainException.cs) |

---

## 1. Inheritance (pewarisan)

### Ada di mana
- [Hotspot.cs:14](../Ignis.Api/Domain/Hotspot.cs#L14): `public abstract class Hotspot` (kelas induk)
- [Hotspot.cs:78](../Ignis.Api/Domain/Hotspot.cs#L78): `public class SatelliteHotspot : Hotspot` (turunan)
- [Hotspot.cs:103](../Ignis.Api/Domain/Hotspot.cs#L103): `public class ManualHotspot : Hotspot` (turunan)
- Pola yang sama pada pengguna: [User.cs](../Ignis.Api/Domain/User.cs), `User` → `Admin` dan `RegisteredUser`.

### Diterapkan lewat apa
`Hotspot` memegang data yang dimiliki **setiap** titik api. Turunan hanya menambah data yang khas untuknya.

| | `Hotspot` (induk) | `SatelliteHotspot` | `ManualHotspot` |
|---|---|---|---|
| Data | `Id`, `Latitude`, `Longitude`, `Confidence`, `Brightness`, `DetectedAt`, `Status` | `SatelliteName`, `Instrument`, `ScanTime` | `ReportedBy`, `Note`, `IncidentTime` |
| Method | `AssignId()`, `ChangeStatus()`, `GetLocation()` | — | `UpdateReport()` |

Konstruktor turunan memanggil konstruktor induk lewat `: base(...)`, sehingga validasi lokasi dan keyakinan dipakai bersama:

```csharp
// ManualHotspot memakai konstruktor induk
: base(HotspotSource.Manual, latitude, longitude, confidence, 0, incidentTime, status)
```

Pewarisan ini juga dibawa ke database. [AppDbContext.cs:88](../Ignis.Api/Data/AppDbContext.cs#L88) memakai `UseTptMappingStrategy()`:
tabel `hotspots` berisi kolom bersama, sedangkan `satellite_hotspots` dan `manual_hotspots` berisi kolom khusus dan berbagi kunci dengan tabel induk.

### Kenapa
- Satelit dan manual sama-sama "titik api". Tanpa pewarisan, validasi lintang/bujur, status, dan lokasi harus ditulis dua kali dan bisa berbeda aturannya.
- Bagian lain sistem (peta, cuaca, prediksi) cukup memakai tipe `Hotspot` tanpa peduli asal datanya.

---

## 2. Polymorphism (satu panggilan, perilaku berbeda)

### Ada di mana
- Deklarasi di induk: [Hotspot.cs:71](../Ignis.Api/Domain/Hotspot.cs#L71) dan [Hotspot.cs:74](../Ignis.Api/Domain/Hotspot.cs#L74)
- Perilaku satelit: [Hotspot.cs:98-99](../Ignis.Api/Domain/Hotspot.cs#L98-L99)
- Perilaku manual: [Hotspot.cs:121-122](../Ignis.Api/Domain/Hotspot.cs#L121-L122)
- Pemakai: [HotspotService.cs:52](../Ignis.Api/Services/HotspotService.cs#L52)

### Diterapkan lewat apa
Induk menjanjikan bahwa setiap `Hotspot` punya `IsEditable` dan `GetDetails()` tanpa mengisinya (`abstract`). Tiap turunan mengisinya sendiri (`override`).

```csharp
// Hotspot.cs (induk)
public abstract bool IsEditable { get; }
public abstract string GetDetails();

// SatelliteHotspot
public override bool IsEditable => false;
public override string GetDetails() => $"Satelit ({SatelliteName} · {Instrument})";

// ManualHotspot
public override bool IsEditable => true;
public override string GetDetails() => $"Manual ({ReportedBy})";
```

Pemanggil memperlakukan semua jenis sebagai `Hotspot`:

```csharp
// HotspotService.DeleteAsync
var existing = await db.Hotspots.FirstOrDefaultAsync(h => h.Id == id, ct);   // bertipe Hotspot
if (!existing.IsEditable)   // polimorfik: satelit false, manual true
    throw new DomainException("Hanya hotspot manual yang dapat dihapus.");
```

Hasilnya sampai ke layar. Nilai `IsEditable` dan `Details` dikirim ke desktop lewat `HotspotDto`, lalu
[HotspotDetailWindow.xaml.cs:18](../Ignis.Frontend/Views/Admin/HotspotDetailWindow.xaml.cs#L18) menonaktifkan tombol Edit dan Hapus untuk hotspot satelit,
dan [Rows.cs:39](../Ignis.Frontend/Models/Admin/Rows.cs#L39) menampilkan teks `Details`.

### Kenapa
- Tanpa polymorphism, `DeleteAsync` akan berisi `if (h is SatelliteHotspot) ... else if (h is ManualHotspot) ...`, dan percabangan serupa tersebar di banyak tempat.
- Dengan polymorphism, menambah jenis hotspot baru (misalnya hotspot dari drone) cukup menambah satu class turunan dan mengisi dua member itu.
  `HotspotService` dan frontend tidak perlu diubah.

---

## 3. Encapsulation (data dilindungi)

### Ada di mana
- Properti: [Hotspot.cs:34-41](../Ignis.Api/Domain/Hotspot.cs#L34-L41), semua `private set` (`DetectedAt` memakai `protected set`)
- Validasi di konstruktor: [Hotspot.cs:22-23](../Ignis.Api/Domain/Hotspot.cs#L22-L23)
- `AssignId`: [Hotspot.cs:44-48](../Ignis.Api/Domain/Hotspot.cs#L44-L48)
- `UpdateReport`: [Hotspot.cs:125-135](../Ignis.Api/Domain/Hotspot.cs#L125-L135)
- Aturan terpusat di `Guard`: [DomainException.cs:10](../Ignis.Api/Domain/DomainException.cs#L10)

### Diterapkan lewat apa
Setter dibuat `private`, jadi kode di luar class tidak bisa menulis `hotspot.Latitude = 999`. Nilai hanya bisa masuk lewat konstruktor atau method yang memvalidasi:

```csharp
// Hotspot.cs: properti tidak bisa diubah dari luar
public double Latitude { get; private set; }
public double Confidence { get; private set; }

// Konstruktor: validasi sebelum nilai disimpan
Guard.Location(latitude, longitude);
Guard.Confidence(confidence);

// Id hanya boleh ditetapkan sekali
public void AssignId(string id)
{
    if (!string.IsNullOrEmpty(Id)) throw new DomainException("Id hotspot sudah ditetapkan.");
    ...
}
```

Aturan yang dijaga:
- lintang -90..90, bujur -180..180, keyakinan 0..100, brightness tidak negatif
- pelapor hotspot manual wajib diisi
- `Id` hanya sekali ditetapkan
- status harus nilai yang dikenal

`UpdateReport` bersifat **atomik**: semua input divalidasi dulu (`Guard.NotBlank`, `ApplyMeasurement`), baru field diubah. Bila satu input salah, tidak ada field yang berubah setengah jalan.

Pelanggaran melempar `DomainException`. [ErrorHandling.cs](../Ignis.Api/Support/ErrorHandling.cs) menerjemahkannya menjadi HTTP 400 dengan pesan siap tampil,
misalnya "Latitude harus berupa angka antara -90 dan 90." yang akan muncul di form admin.

### Kenapa
- Objek `Hotspot` tidak mungkin berada dalam keadaan tidak valid, apa pun jalur masuknya: form desktop, impor NASA FIRMS, atau API langsung.
  Aturannya dijaga oleh objek itu sendiri, bukan oleh tiap pemanggil.
- Saat ada bug data, penyebabnya mudah dilacak karena hanya sedikit tempat yang bisa mengubah nilai.

---

## Bukti pengujian

Jalankan dari root repo:

```bash
dotnet test Ignis.Tests --filter "HotspotOopTests"
```

| Konsep | Tes | Lokasi |
|---|---|---|
| Inheritance | `Subclasses_AreHotspots`, `Users_InheritFromUser` | [DomainTests.cs:18](../Ignis.Tests/DomainTests.cs#L18) |
| Polymorphism | `SameCall_DifferentBehavior_PerSubclass` | [DomainTests.cs:35](../Ignis.Tests/DomainTests.cs#L35) |
| Polymorphism | `Delete_UsesPolymorphicIsEditable` | [ServiceTests.cs:62](../Ignis.Tests/ServiceTests.cs#L62) |
| Encapsulation | `Constructor_RejectsInvalidInput`, `Id_CanOnlyBeAssignedOnce` | [DomainTests.cs:57](../Ignis.Tests/DomainTests.cs#L57) |
| Encapsulation | `Update_IsAtomic_InvalidInputChangesNothing` | [DomainTests.cs:74](../Ignis.Tests/DomainTests.cs#L74) |

## Cara membuktikan lewat aplikasi

1. Jalankan API dan desktop (lihat [README](../README.md)), login `admin@ignis.example` / `Ignis@123`.
2. **Encapsulation**: buka **Kelola Hotspot → Tambah Hotspot**, isi lintang `200`, lalu simpan. Pesan error dari `Guard.Location` muncul dan tidak ada data yang tersimpan.
3. **Polymorphism**: buka detail hotspot satelit (mis. `HS-0142`). Tombol Edit dan Hapus nonaktif. Buka hotspot manual (mis. `HS-0138`), tombolnya aktif. Teks sumber berbeda ("Satelit (...)" dan "Manual (...)").
4. **Inheritance**: lihat database setelah menambah hotspot manual: `SELECT * FROM hotspots h JOIN manual_hotspots m USING (hotspot_id);`.
   Satu hotspot tersimpan di tabel induk dan tabel turunan sekaligus.
