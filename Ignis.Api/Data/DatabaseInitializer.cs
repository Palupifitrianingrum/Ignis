using Ignis.Api.Domain;
using Ignis.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Api.Data;

/// <summary>
/// Menyiapkan database saat API start. Pengaturan (appsettings / .env):
///  - Storage:AutoCreate    (default true)  : PostgreSQL — buat tabel dan sequence Id bila belum ada.
///  - Storage:SeedDemoData  (default: true untuk InMemory, false untuk Postgres) : isi data contoh bila tabel masih kosong.
///  - Admin:Email / Admin:Password / Admin:Name : buat akun admin pertama bila belum ada
///    (cara membuat admin di server nyata tanpa data demo).
/// TODO(backend): untuk produksi ganti EnsureCreated dengan EF migration.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var postgres = db.Database.IsNpgsql();

        if (!postgres || config.GetValue("Storage:AutoCreate", true))
        {
            await db.Database.EnsureCreatedAsync(ct);
            if (postgres) await CreateIdSequencesAsync(db, ct);
        }

        if (config.GetValue("Storage:SeedDemoData", !postgres)) await SeedAsync(db, log, ct);
        await EnsureAdminAsync(db, config, log, ct);
    }

    /// <summary>Sequence untuk Id (HS-, WX-, PR-, U-). Idempoten; juga menutup kasus database lama yang dibuat sebelum sequence ada.</summary>
    private static async Task CreateIdSequencesAsync(AppDbContext db, CancellationToken ct)
    {
        foreach (var name in AppDbContext.SequenceNames)   // nama = konstanta di AppDbContext, bukan input pengguna
        {
            var sql = "CREATE SEQUENCE IF NOT EXISTS " + name + " START " + AppDbContext.FirstGeneratedNumber;
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
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

        log.LogWarning("Data demo dimasukkan. Semua akun demo memakai password '{Password}' — jangan dipakai di produksi.", DemoData.Password);
    }

    private static async Task EnsureAdminAsync(AppDbContext db, IConfiguration config, ILogger log, CancellationToken ct)
    {
        var email = config["Admin:Email"];
        var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return;

        var admin = new Admin(await db.NextIdAsync(IdKind.User, ct), config["Admin:Name"] ?? "Administrator", email, Passwords.Hash(password));
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Akun admin {Email} dibuat dari konfigurasi Admin:*.", email);
    }
}
