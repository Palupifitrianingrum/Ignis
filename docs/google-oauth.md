# Login dengan Google

Kodenya sudah ada. Yang perlu Anda siapkan hanya **Client ID** dari Google Cloud (gratis, sekitar 10 menit).
Tanpa Client ID, tombol "Login dengan Google" menampilkan "Login Google belum dikonfigurasi di server" dan login email tetap berfungsi.

Jalur sukses (kode ditukar dengan Google sungguhan) baru bisa dicoba setelah Client ID terpasang. Yang sudah diuji otomatis:
semua logika di sisi kita dengan Google palsu (akun baru, penautan email, akun nonaktif, pembajakan awal) dan penerjemahan
pesan error dari Google, termasuk bentuk error asli yang teramati langsung dari endpoint Google
(`HTTP 401 {"error":"invalid_client","error_description":"The OAuth client was not found."}`).

> Sumber: dokumentasi resmi Google, "OAuth 2.0 for iOS & Desktop Apps" dan "OpenID Connect" (developers.google.com/identity).

## Membuat Client ID di Google Cloud

Tampilan Console sering berubah. Menu sekarang bernama **Google Auth platform** (dulu "OAuth consent screen").

1. Buka https://console.cloud.google.com dan login.
2. Pemilih proyek di bar atas, **New Project**, nama `Ignis`, **Create**, lalu pastikan proyek itu terpilih.
3. Buka **Google Auth platform** (atau cari "Google Auth platform" di kotak pencarian), klik **Get started**:
   - **App information**: App name `Ignis`, User support email = email Anda.
   - **Audience**: pilih **External**.
   - **Contact information**: email Anda. Setujui kebijakan, **Create**.
4. Di **Data Access**, scope yang dipakai hanya `openid`, `.../auth/userinfo.email`, `.../auth/userinfo.profile`
   (scope dasar; tidak perlu verifikasi aplikasi dari Google).
5. Di **Audience**, status awal **Testing**. Menurut dokumentasi Google, aplikasi yang hanya meminta scope dasar (sign-in) tidak
   dibatasi daftar test user dan tidak kedaluwarsa 7 hari. **Bila Google menjawab `access_denied` saat Anda mencoba login**
   (belum teramati langsung karena butuh akun sungguhan), tambahkan email Anda di **Audience → Test users → Add users**
   (maksimal 100), atau klik **Publish app** supaya siapa pun bisa login.
6. Buka **Clients** → **Create client**:
   - **Application type**: **Desktop app** (bukan "Web application": jenis yang salah menyebabkan `redirect_uri_mismatch`).
   - **Name**: `Ignis Desktop`, **Create**.
7. Salin **Client ID** (berakhiran `.apps.googleusercontent.com`). Client secret juga ditampilkan (lihat catatan di bawah).

Tidak ada "Authorized redirect URI" yang perlu diisi: client Desktop otomatis mengizinkan alamat loopback `http://127.0.0.1:<port>`.
(Menurut Google, custom URI scheme dan alur OOB tidak didukung lagi; loopback adalah cara yang dipakai aplikasi ini.)

## Memasang di server

Di `.env` (root repo, tidak ikut Git):

```
Google__ClientId=1234567890-abcdefg.apps.googleusercontent.com
Google__ClientSecret=GOCSPX-xxxxxxxxxxxxxxxx
```

- **Client ID wajib.** Client secret **opsional**: Google menyebut aplikasi desktop sebagai "public client" yang tidak bisa menyimpan
  rahasia (pengamannya PKCE). Isi secret hanya bila Google menolak dengan pesan "client_secret is missing"; server akan mengirimnya.
- Restart `Ignis.Api`. Cek: `GET http://localhost:5000/api/auth/google/config` mengembalikan `{"clientId":"...apps.googleusercontent.com"}` (bukan `null`).
- Aplikasi desktop tidak perlu diubah atau diberi kunci apa pun.
- Jangan commit secret, jangan tempel di chat, jangan taruh di `appsettings.json`. Bila bocor: **Reset secret** di halaman client.

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
    FE->>FE: buat state + code_verifier + code_challenge (PKCE S256), buka listener 127.0.0.1:port
    FE->>G: buka browser: halaman izin Google
    U->>G: pilih akun, setuju
    G-->>FE: redirect ke 127.0.0.1:port?code=...&state=...
    FE->>API: POST /api/auth/google {code, codeVerifier, redirectUri}
    API->>G: tukar code + verifier (+ secret bila ada) jadi id_token
    G-->>API: id_token
    API->>API: verifikasi id_token (tanda tangan, issuer, audience = Client ID, masa berlaku, email terverifikasi)
    API->>API: cari akun dari "sub"; kalau belum ada, dari email; kalau belum ada, buat RegisteredUser
    API-->>FE: token JWT Ignis + profil
```

Aturan akun:

- **Kunci identitas adalah `sub`** (id akun Google yang tetap), bukan email: email bisa berubah. Kolom `users.google_subject` (unik).
- Email Google sudah ada sebagai akun Ignis: akun itu **ditautkan** ke Google (hanya bila email terverifikasi oleh Google).
- **Anti pembajakan awal**: pendaftaran biasa tidak memverifikasi email, jadi orang lain bisa mendaftar lebih dulu dengan email korban.
  Saat pemilik email sungguhan login lewat Google, password lama pada akun pengguna terdaftar **dibuang** dan semua token lama dicabut;
  pemilik masuk lewat Google dan bisa membuat password baru di Pengaturan. Akun Admin tidak diubah (admin dibuat operator).
- Akun baru dari Google selalu role `Registered`, tanpa password. Menjadikannya Admin: lewat halaman Pengguna oleh admin lain.
- Akun nonaktif tetap ditolak.

## Masalah umum

| Pesan / gejala | Penyebab | Perbaikan |
|---|---|---|
| "Login Google belum dikonfigurasi di server" | `Google__ClientId` kosong | Isi di `.env`, restart API |
| "Google tidak mengenali klien OAuth ini" (`invalid_client`) | Client ID salah/salah proyek, atau klien dihapus | Salin ulang Client ID dari halaman Clients |
| `redirect_uri_mismatch` | Jenis klien bukan **Desktop app** | Buat klien baru jenis Desktop app |
| "Google meminta client secret" | Klien Anda mensyaratkan secret | Isi `Google__ClientSecret`, restart |
| "Kode login Google tidak berlaku lagi" (`invalid_grant`) | Kode kedaluwarsa/terpakai, atau jam komputer meleset jauh | Ulangi login; periksa jam |
| Halaman Google: `access_denied` / "app not verified" | Aplikasi Testing dan akun Anda bukan test user | Tambahkan email di Audience → Test users, atau Publish app |
| "Email akun Google belum terverifikasi" | Akun Google tanpa email terverifikasi | Gunakan akun lain |
| "Tidak dapat menghubungi Google" | Server API tanpa internet | Periksa koneksi server |
