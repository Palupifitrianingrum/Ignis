# Autentikasi, otorisasi, dan role

## Role dan hak akses

| Kemampuan | Anonim | Registered | Admin |
|---|:-:|:-:|:-:|
| Daftar, login (email / Google), `google/config` | ya | ya | ya |
| Lihat hotspot, cuaca, prediksi, simulasi | – | ya | ya |
| Lokasi tersimpan dan notifikasi milik sendiri | – | ya | – (sesuai ERD) |
| Ganti password sendiri, logout, refresh | – | ya | ya |
| Tambah/ubah/hapus hotspot manual, sinkronisasi, log aktivitas | – | – | ya |
| Daftar pengguna, aktif/nonaktifkan, **ubah role** | – | – | ya |
| Ubah API key FIRMS / OpenWeather, status integrasi | – | – | ya |

Matriks ini dijaga tes otomatis: `EveryApiRoute_IsEitherWhitelistedAnonymous_OrRequiresAuthorization` menelusuri semua route `/api`
dan gagal bila ada yang tidak berotorisasi dan tidak ada di daftar putih anonim.

## Alur sesi

```mermaid
sequenceDiagram
    participant FE as Desktop
    participant API as Ignis.Api
    participant DB as Database
    FE->>API: POST /api/auth/login
    API->>DB: cek akun, hash password
    API-->>FE: JWT (60 menit; klaim sub, role, ver, auth_time)
    loop tiap request
        FE->>API: Authorization: Bearer JWT
        API->>DB: akun masih ada, aktif, role sama, ver sama?
    end
    FE->>API: POST /api/auth/refresh (pada ~75% masa berlaku)
    API-->>FE: JWT baru (auth_time asli dipertahankan)
    FE->>API: POST /api/auth/logout
    API->>DB: token_version + 1  (semua token lama mati)
```

Sifat keamanan:

- **Pencabutan di sisi server.** Setiap token memuat nomor versi (`ver`) yang harus sama dengan `users.token_version`. Nomor dinaikkan oleh:
  logout, ganti password, nonaktifkan akun, ganti role, dan pembuangan password saat penautan Google. Efeknya langsung pada request berikutnya (bukan menunggu token habis).
- **Batas sesi.** Refresh memperpanjang 60 menit sekali, tetapi total sesi dibatasi `Jwt:MaxSessionHours` (12 jam sejak login); sesudahnya login ulang.
- **Password**: PBKDF2 + salt (ASP.NET Core Identity). Kebijakan mengikuti NIST SP 800-63B: 8–128 karakter, bukan password umum, tidak sama dengan email/nama, tanpa aturan komposisi.
- **Login gagal**: pesan sama untuk email tak dikenal dan password salah. 5 kegagalan berturut-turut per email mengunci 5 menit (makin lama bila berulang, maksimal 60 menit); berlaku sama untuk email yang tidak terdaftar.
  Di atasnya ada pembatas laju per IP (`Auth:RateLimitPerMinute`, bawaan 20/menit) untuk login, daftar, Google, dan ganti password. Keduanya menjawab **HTTP 429 + `Retry-After`** (bukan 401, supaya aplikasi tidak mengira sesinya berakhir).
- **Ganti password salah** dijawab 400 (bukan 401) dengan alasan yang sama.
- **Ubah role** (`PUT /api/users/{id}/role`): tidak boleh mengubah role sendiri, admin aktif terakhir tidak boleh diturunkan, token pengguna itu dicabut.
  Mengangkat pengguna terdaftar menjadi admin **menghapus lokasi tersimpan dan notifikasinya** (admin tidak punya itu). Aplikasi meminta konfirmasi.
- **Google**: lihat [google-oauth.md](google-oauth.md) (kunci `sub`, penautan email, anti pembajakan awal).

## Endpoint

| Method | Route | Akses |
|---|---|---|
| POST | `/api/auth/register`, `/login`, `/google` | anonim (dibatasi laju) |
| GET | `/api/auth/google/config` | anonim |
| GET | `/api/auth/me` | login |
| POST | `/api/auth/logout`, `/refresh` | login |
| POST | `/api/auth/change-password` | login (dibatasi laju) |
| PUT | `/api/users/{id}/role` | Admin |

## Konfigurasi

| Kunci | Bawaan | Arti |
|---|---|---|
| `Jwt:Key` | acak saat start | Minimal 32 karakter; isi agar token bertahan antar-restart |
| `Jwt:ExpiresMinutes` | 60 | Umur satu token |
| `Jwt:MaxSessionHours` | 12 | Batas total sesi sejak login |
| `Auth:MaxFailedAttempts` | 5 | Kegagalan sebelum akun dikunci |
| `Auth:LockoutMinutes` | 5 | Lama kunci pertama (dikali 2, 4, 8, 12 bila berulang) |
| `Auth:RateLimitEnabled` | true | Matikan **hanya** untuk tes |
| `Auth:RateLimitPerMinute` | 20 | Permintaan per IP per menit |
| `Google__ClientId` / `Google__ClientSecret` | kosong | Lihat google-oauth.md |

## Batasan yang disengaja

- **Kunci akun bisa dipakai menyerang** (sengaja gagal login ke akun orang lain mengunci akun itu sementara). Kuncinya singkat dan per akun, jadi dampaknya terbatas.
- Kunci login dan pembatas laju disimpan di **memori proses**: hilang saat server restart dan tidak dibagi antar-instance.
- Pendaftaran menampilkan "Email sudah terdaftar" (membocorkan email terdaftar); diterima demi pengalaman pengguna.
- Registrasi tidak memverifikasi email (tidak ada pengiriman email); pembajakan awal ditangani saat penautan Google.
- **HTTPS belum dikonfigurasi** (TODO B9). Token dikirim lewat header, jadi untuk jaringan nyata wajib di belakang TLS. Untuk `localhost` aman.
- Token yang diterbitkan sebelum pembaruan ini tidak memuat `ver`, sehingga ditolak: semua pengguna login ulang sekali. Kolom baru (`token_version`, `google_subject`) ditambahkan otomatis pada start untuk database yang sudah ada.
