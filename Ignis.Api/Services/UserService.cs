using Ignis.Api.Data;
using Ignis.Api.Domain;
using Ignis.Shared;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Api.Services;

/// <summary>Daftar dan status aktif pengguna (halaman admin). Login/registrasi ada di <see cref="AuthService"/>.</summary>
public sealed class UserService(AppDbContext db, ActivityLogService log)
{
    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct);

    /// <summary>Aktif/nonaktifkan pengguna. Mengembalikan pesan error (null bila sukses). Admin aktif terakhir tidak boleh dinonaktifkan.</summary>
    public async Task<string?> ToggleActiveAsync(string id, CancellationToken ct = default)
    {
        var all = await db.Users.ToListAsync(ct);
        var u = all.FirstOrDefault(x => x.Id == id);
        if (u == null) return "Pengguna tidak ditemukan.";
        if (u.IsActive && u.Role == Role.Admin && all.Count(x => x.Role == Role.Admin && x.IsActive) <= 1)
            return "Tidak bisa menonaktifkan satu-satunya admin aktif.";

        if (u.IsActive) u.Deactivate(); else u.Activate();
        await db.SaveChangesAsync(ct);
        await log.AddAsync($"{u.Name} {(u.IsActive ? "diaktifkan" : "dinonaktifkan")}", ct);
        return null;
    }
}
