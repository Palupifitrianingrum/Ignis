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

    /// <summary>
    /// Ganti role pengguna (Registered &lt;-&gt; Admin). Mengembalikan pesan error (null bila sukses).
    /// Aturan: tidak bisa mengubah role sendiri, dan admin aktif terakhir tidak boleh diturunkan.
    /// Role adalah tipe baris (pewarisan TPT), jadi baris diganti dengan id yang sama; token lama dicabut sehingga hak akses lama langsung hilang.
    /// Admin tidak punya lokasi tersimpan/notifikasi (ERD), jadi mengangkat pengguna menjadi admin MENGHAPUS data itu.
    /// </summary>
    public async Task<string?> SetRoleAsync(string actorId, string id, Role role, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(role)) return "Role tidak dikenal.";
        if (actorId == id) return "Anda tidak bisa mengubah role akun Anda sendiri.";

        var all = await db.Users.ToListAsync(ct);
        var u = all.FirstOrDefault(x => x.Id == id);
        if (u == null) return "Pengguna tidak ditemukan.";
        if (u.Role == role) return null;
        if (u.Role == Role.Admin && u.IsActive && all.Count(x => x.Role == Role.Admin && x.IsActive) <= 1)
            return "Tidak bisa menurunkan satu-satunya admin aktif.";

        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        if (role == Role.Admin)
        {
            db.Notifications.RemoveRange(await db.Notifications.Where(n => n.UserId == id).ToListAsync(ct));
            db.MonitoringAreas.RemoveRange(await db.MonitoringAreas.Where(a => a.UserId == id).ToListAsync(ct));
        }
        var replacement = u.ConvertTo(role);
        db.Users.Remove(u);
        await db.SaveChangesAsync(ct);
        db.Users.Add(replacement);
        await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);

        await log.AddAsync($"Role {replacement.Name} diubah menjadi {role}", ct);
        return null;
    }
}
