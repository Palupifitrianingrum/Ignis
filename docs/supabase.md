# Memakai Supabase sebagai database

Ignis.Api tetap berbicara langsung ke PostgreSQL (Npgsql/EF Core). Supabase hanya menjadi server PostgreSQL-nya;
kita **tidak** memakai REST API, Auth, atau kunci anon/service-role Supabase.

## Memasang

1. Di dasbor Supabase buka proyek Anda, klik **Connect** di bagian atas.
2. Pilih **Session pooler** (port 5432) dan salin URI-nya. Pakai Session pooler, bukan "Direct connection":
   koneksi langsung Supabase hanya IPv6 pada paket gratis dan sering gagal dari jaringan rumah/kampus.
   Jangan pakai Transaction pooler (port 6543).
3. Ganti `[YOUR-PASSWORD]` dengan password database proyek (yang Anda buat saat membuat proyek; bisa direset di **Project Settings → Database**).
4. Tempel di `.env` (Anda sendiri, jangan ke chat):

   ```
   Supabase__DbUrl=postgresql://postgres.abcdefgh:PASSWORD@aws-0-ap-southeast-1.pooler.supabase.com:5432/postgres
   ```
   Jika password memuat karakter khusus (`@ : / ? #`), ganti passwordnya ke huruf+angka saja, atau encode-persen karakter itu.
5. Jalankan `Ignis.Api`. Saat start API membuat tabel, sequence, dan kolom yang kurang secara otomatis (`Storage__AutoCreate=true`).
   Cek: `GET /health` menjawab `"storage":"Supabase"`, dan tabel muncul di **Table Editor**.

Data demo (opsional): `Storage__SeedDemoData=true` hanya mengisi bila tabel kosong. Jangan dibiarkan `true` untuk data sungguhan.
Admin pertama tanpa data demo: `Admin__Email`, `Admin__Password`, `Admin__Name`.

## Kembali ke PostgreSQL lokal

Kosongkan nilai `Supabase__DbUrl=` di `.env` (biarkan barisnya). API otomatis memakai `Storage__Provider=Postgres` dan `ConnectionStrings__Ignis`
(Docker: `docker compose up -d`). Urutan pemilihan:

| Kondisi | Database |
|---|---|
| `Storage__Provider=InMemory` (eksplisit) | InMemory (menang atas semuanya; dipakai tes) |
| `Supabase__DbUrl` terisi | Supabase |
| `Supabase__DbUrl` kosong dan `Storage__Provider=Postgres` | PostgreSQL lokal |
| tidak ada yang diisi | InMemory |

Data di Supabase dan di lokal terpisah; pindah database berarti data tidak ikut pindah.

## Keamanan

- Supabase membuka semua tabel di skema `public` lewat REST API publik. Karena itu saat terhubung ke Supabase, API **mengaktifkan Row Level Security
  (tanpa policy) di semua tabel** pada setiap start, sehingga kunci anon tidak bisa membaca tabel `users` (hash password). API sendiri tidak terpengaruh
  karena terhubung sebagai role database, bukan lewat REST API.
- Koneksi memakai SSL (wajib). Sertifikat Supabase bukan dari CA publik, jadi koneksi dienkripsi tetapi sertifikat server tidak divalidasi.
- Password database = kunci penuh ke data. Jangan commit `.env`, jangan tempel di chat; bila bocor, reset di Project Settings → Database.
