# Login dengan Google

Kodenya sudah ada (lihat "Cara kerja"). Yang belum ada hanya **kunci** dari Google Cloud.
Tanpa kunci, tombol "Login Google" menampilkan "Login Google belum dikonfigurasi di server" dan login email tetap berfungsi.
Yang sudah diuji otomatis hanya kondisi tanpa kunci; pertukaran kode dengan Google sungguhan baru bisa diuji setelah kunci terpasang,
jadi laporkan bila ada error saat percobaan pertama.

## Membuat kunci di Google Cloud (sekitar 10 menit, gratis)

Tampilan Google Cloud Console sering berubah; nama menu di bawah mengikuti versi saat ini (menu "Google Auth Platform"
menggantikan "OAuth consent screen"). Bila ada yang beda, cari kata kuncinya.

1. Buka https://console.cloud.google.com dan login dengan akun Google Anda.
2. Klik pemilih proyek di bar atas, **New Project**, beri nama `Ignis`, **Create**. Pastikan proyek itu terpilih.
3. Buka menu **APIs & Services → OAuth consent screen** (atau **Google Auth Platform**), klik **Get started**:
   - **App name**: `Ignis`. **User support email**: email Anda.
   - **Audience**: pilih **External**.
   - **Contact information**: email Anda. Setujui kebijakan, **Create**.
4. Di bagian **Data Access** (atau **Scopes**), tambahkan scope `openid`, `.../auth/userinfo.email`, dan `.../auth/userinfo.profile`.
   Ketiganya scope dasar, jadi tidak perlu verifikasi aplikasi dari Google.
5. Di bagian **Audience**, status awal aplikasi adalah **Testing**. Pada status ini **hanya akun yang didaftarkan** di **Test users**
   yang bisa login. Klik **Add users**, masukkan email Google yang akan dipakai menguji (maksimal 100).
   Setelah siap dipakai umum, klik **Publish app** (ubah ke *In production*). Untuk scope dasar tidak perlu review.
6. Buka **Credentials** (atau **Clients**) → **Create credentials → OAuth client ID**:
   - **Application type**: **Desktop app** (bukan "Web application"; jenis yang salah menyebabkan error `redirect_uri_mismatch`).
   - **Name**: `Ignis Desktop`. Klik **Create**.
7. Jendela hasil menampilkan **Client ID** (berakhiran `.apps.googleusercontent.com`) dan **Client secret**. Salin keduanya
   (atau **Download JSON**). Secret bisa dilihat lagi nanti di halaman client.

Tidak ada "Authorized redirect URI" yang perlu diisi: client jenis Desktop otomatis mengizinkan alamat loopback `http://127.0.0.1:<port>`
yang dipakai aplikasi.

## Memasang kunci di server

Di file `.env` (root repo, tidak ikut Git), tambahkan:

```
Google__ClientId=1234567890-abcdefg.apps.googleusercontent.com
Google__ClientSecret=GOCSPX-xxxxxxxxxxxxxxxx
```

Restart `Ignis.Api`. Cek: `GET http://localhost:5000/api/auth/google/config` mengembalikan `{"clientId":"1234...apps.googleusercontent.com"}`
(bukan `null`). Kunci cukup ada di **server**; aplikasi desktop tidak perlu diubah atau diberi kunci apa pun.

Jangan commit Client secret, tempel di chat, atau menaruhnya di `appsettings.json`. Bila bocor, buka halaman client di Google Cloud
dan buat secret baru (**Reset secret**).

## Cara kerja

```mermaid
sequenceDiagram
    actor U as Pengguna
    participant FE as Aplikasi desktop
    participant G as Google
    participant API as Ignis.Api

    U->>FE: klik "Login dengan Google"
    FE->>API: GET /api/auth/google/config
    API-->>FE: clientId
    FE->>FE: buat code_verifier + code_challenge (PKCE), buka listener 127.0.0.1:port
    FE->>G: buka browser: halaman izin Google
    U->>G: pilih akun, setuju
    G-->>FE: redirect ke 127.0.0.1:port?code=...
    FE->>API: POST /api/auth/google {code, codeVerifier, redirectUri}
    API->>G: tukar code + client secret + verifier jadi id_token
    G-->>API: id_token
    API->>API: verifikasi id_token (tanda tangan, audience, email terverifikasi)
    API->>API: cari pengguna dari email; belum ada = buat RegisteredUser
    API-->>FE: token JWT Ignis + profil
```

- Penukaran `code` terjadi di **server**, jadi Client secret tidak pernah sampai ke komputer pengguna.
- PKCE (`code_verifier`) mencegah `code` yang dicuri dari browser dipakai pihak lain.
- Akun yang dibuat lewat Google selalu role `Registered` dan tidak punya password. Untuk menjadikannya Admin, ubah role di database
  (belum ada layar untuk itu).
- Bila email itu sudah terdaftar lewat email+password, login Google masuk ke akun yang sama (Google sudah memverifikasi emailnya).
- Akun yang dinonaktifkan admin tetap ditolak.

## Masalah umum

| Gejala | Penyebab | Perbaikan |
|---|---|---|
| "Login Google belum dikonfigurasi di server" | `Google__ClientId`/`Google__ClientSecret` kosong atau server belum di-restart | Isi `.env`, restart API |
| Google menampilkan "Access blocked: ... has not completed the Google verification process" / error 403 `access_denied` | Aplikasi berstatus Testing dan email Anda belum jadi Test user | Tambahkan email di **Audience → Test users**, atau Publish app |
| `redirect_uri_mismatch` | Client dibuat sebagai "Web application" | Buat ulang sebagai **Desktop app** |
| "Google menolak kode login" | Client ID/secret salah, atau jam komputer server melenceng | Periksa nilai `.env`; sinkronkan jam |
| Browser tidak terbuka / "tidak diselesaikan dalam 3 menit" | Login tidak diselesaikan, atau firewall memblokir listener lokal | Coba lagi; izinkan aplikasi di firewall |
