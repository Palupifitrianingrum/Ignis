using Ignis.Shared;

namespace Ignis.Api.Domain;

/// <summary>
/// Pengguna aplikasi. Abstrak: <see cref="Admin"/> dan <see cref="RegisteredUser"/> menurunkannya (sesuai ERD: USER, ADMIN, REGISTERED_USER).
/// Status aktif hanya berubah lewat <see cref="Activate"/>/<see cref="Deactivate"/> (enkapsulasi).
/// </summary>
public abstract class User
{
    protected User(Role role) => Role = role;

    protected User(Role role, string id, string name, string email, string passwordHash, DateTime? createdAt = null) : this(role)
    {
        Guard.NotBlank(id, "Id pengguna wajib diisi.");
        Guard.NotBlank(name, "Nama pengguna wajib diisi.");
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) throw new DomainException("Email tidak valid.");

        Id = id;
        Name = name.Trim();
        Email = email.Trim();
        PasswordHash = passwordHash ?? string.Empty;
        CreatedAt = createdAt ?? DateTime.Now;
    }

    public string Id { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public Role Role { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>Kolom tambahan di luar ERD: dipakai fitur aktif/nonaktifkan pengguna di halaman admin.</summary>
    public bool IsActive { get; private set; } = true;

    public void Activate() => IsActive = true;

    /// <summary>Nonaktifkan akun. Sekaligus mencabut semua token yang sudah terbit, supaya efeknya langsung (tidak menunggu token kedaluwarsa).</summary>
    public void Deactivate()
    {
        IsActive = false;
        RevokeTokens();
    }

    /// <summary>
    /// Nomor versi token. Setiap token memuat nomor ini; token hanya berlaku bila sama dengan nomor sekarang.
    /// Menaikkannya (logout, ganti password, nonaktif, ganti role) mencabut semua token lama sekaligus.
    /// </summary>
    public int TokenVersion { get; private set; }

    public void RevokeTokens() => TokenVersion++;

    /// <summary>Id akun Google (klaim "sub"): pengenal yang stabil, berbeda dari email yang bisa berubah. Null bila belum tertaut.</summary>
    public string? GoogleSubject { get; private set; }

    public bool HasPassword => PasswordHash.Length > 0;

    public void LinkGoogle(string subject)
    {
        Guard.NotBlank(subject, "Id akun Google tidak boleh kosong.");
        GoogleSubject = subject;
    }

    /// <summary>Ganti password. Semua token lama dicabut.</summary>
    public void ChangePasswordHash(string newHash)
    {
        Guard.NotBlank(newHash, "Hash password tidak boleh kosong.");
        PasswordHash = newHash;
        RevokeTokens();
    }

    /// <summary>Hapus password (akun hanya bisa masuk lewat Google). Semua token lama dicabut.</summary>
    public void ClearPassword()
    {
        PasswordHash = string.Empty;
        RevokeTokens();
    }

    /// <summary>
    /// Salinan akun ini dengan role lain (Id, nama, email, password, tautan Google, status, dan tanggal dibuat dipertahankan).
    /// Dibutuhkan karena Admin dan RegisteredUser adalah tipe berbeda (tabel berbeda): mengubah role = mengganti barisnya.
    /// Token lama dicabut (versi naik).
    /// </summary>
    public User ConvertTo(Role newRole)
    {
        User copy = newRole == Role.Admin
            ? new Admin(Id, Name, Email, PasswordHash, CreatedAt)
            : new RegisteredUser(Id, Name, Email, PasswordHash, CreatedAt);

        copy.TokenVersion = TokenVersion + 1;
        copy.GoogleSubject = GoogleSubject;
        if (!IsActive) copy.IsActive = false;
        return copy;
    }

    /// <summary>Boleh mengelola (tambah/ubah/hapus) hotspot? (polimorfik)</summary>
    public abstract bool CanManageHotspots { get; }
}

public class Admin : User
{
    private Admin() : base(Role.Admin) { }   // untuk EF Core

    public Admin(string id, string name, string email, string passwordHash, DateTime? createdAt = null)
        : base(Role.Admin, id, name, email, passwordHash, createdAt) { }

    public override bool CanManageHotspots => true;
}

public class RegisteredUser : User
{
    private RegisteredUser() : base(Role.Registered) { }   // untuk EF Core

    public RegisteredUser(string id, string name, string email, string passwordHash, DateTime? createdAt = null)
        : base(Role.Registered, id, name, email, passwordHash, createdAt) { }

    public override bool CanManageHotspots => false;
}
