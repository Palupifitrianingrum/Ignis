using Ignis.Core.InMemory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ignis.Infrastructure.Persistence;

/// <summary>
/// Menyiapkan database saat API start (hanya untuk Storage:Provider=Postgres):
///  - Storage:AutoCreate (default true): buat tabel bila belum ada (EnsureCreated).
///  - Storage:SeedDemoData (default false): isi data contoh bila tabel masih kosong.
/// TODO(backend): untuk produksi ganti EnsureCreated dengan EF migration.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, CancellationToken ct = default)
    {
        if (!(config["Storage:Provider"] ?? "InMemory").Equals("Postgres", StringComparison.OrdinalIgnoreCase)) return;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");

        if (config.GetValue("Storage:AutoCreate", true))
        {
            await db.Database.EnsureCreatedAsync(ct);
            log.LogInformation("Skema database siap.");
        }

        if (config.GetValue("Storage:SeedDemoData", false)) await SeedAsync(db, log, ct);
    }

    private static async Task SeedAsync(AppDbContext db, ILogger log, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct) || await db.Hotspots.AnyAsync(ct))
        {
            log.LogInformation("Seed dilewati: tabel sudah berisi data.");
            return;
        }

        // Urutan penting karena foreign key: users, hotspots, lalu predictions.
        db.Users.AddRange(DemoData.Users());
        db.Hotspots.AddRange(DemoData.Hotspots());
        await db.SaveChangesAsync(ct);

        db.Predictions.AddRange(DemoData.Predictions());
        foreach (var a in DemoData.Activities())
            db.ActivityLog.Add(new ActivityLogEntry { Message = a[..a.LastIndexOf(" · ", StringComparison.Ordinal)], CreatedAt = DateTime.Now });
        await db.SaveChangesAsync(ct);

        log.LogInformation("Data demo dimasukkan ke database.");
    }
}
