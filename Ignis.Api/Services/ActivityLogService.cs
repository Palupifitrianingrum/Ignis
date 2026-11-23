using Ignis.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Api.Services;

/// <summary>Log aktivitas ringkas yang ditampilkan di halaman admin ("Admin menambah hotspot ...").</summary>
public sealed class ActivityLogService(AppDbContext db)
{
    public async Task AddAsync(string message, CancellationToken ct = default)
    {
        db.ActivityLog.Add(new ActivityLogEntry { Message = message, CreatedAt = DateTime.Now });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Entri terbaru di atas, berformat "pesan · HH:mm".</summary>
    public async Task<IReadOnlyList<string>> RecentAsync(int count = 30, CancellationToken ct = default)
    {
        var rows = await db.ActivityLog.AsNoTracking().OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(count).ToListAsync(ct);
        return rows.Select(a => $"{a.Message} · {a.CreatedAt:HH:mm}").ToList();
    }
}
