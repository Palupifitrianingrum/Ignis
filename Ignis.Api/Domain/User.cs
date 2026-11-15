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
    public void Deactivate() => IsActive = false;

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
