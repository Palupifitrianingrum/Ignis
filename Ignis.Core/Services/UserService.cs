using Ignis.Core.Abstractions;
using Ignis.Core.Entities;
using Ignis.Core.Enums;

namespace Ignis.Core.Services;

public sealed class UserService
{
    private readonly IUserRepository _repo;
    private readonly IActivityLog _log;

    public UserService(IUserRepository repo, IActivityLog log)
    {
        _repo = repo;
        _log = log;
    }

    public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) => _repo.GetAllAsync(ct);

    /// <summary>Aktif/nonaktifkan pengguna. Mengembalikan pesan error (null bila sukses). Admin aktif terakhir tidak boleh dinonaktifkan.</summary>
    public async Task<string?> ToggleActiveAsync(string id, CancellationToken ct = default)
    {
        var all = await _repo.GetAllAsync(ct);
        var u = all.FirstOrDefault(x => x.Id == id);
        if (u == null) return "Pengguna tidak ditemukan.";
        if (u.IsActive && u.Role == Role.Admin && all.Count(x => x.Role == Role.Admin && x.IsActive) <= 1)
            return "Tidak bisa menonaktifkan satu-satunya admin aktif.";

        if (u.IsActive) u.Deactivate(); else u.Activate();
        await _repo.UpdateAsync(u, ct);
        await _log.AddAsync($"{u.Name} {(u.IsActive ? "diaktifkan" : "dinonaktifkan")}", ct);
        return null;
    }
}
