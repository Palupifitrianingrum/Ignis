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
            if (postgres)
            {
                await CreateIdSequencesAsync(db, ct);
                await EnsureAppSettingsTableAsync(db, ct);
                await EnsureNotificationColumnsAsync(db, ct);
            }
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

    /// <summary>
    /// Tabel app_settings (override key). EnsureCreated tidak menambah tabel ke database yang sudah ada, jadi dibuat di sini bila belum ada
    /// (sampai migration, TODO B4). Skema harus sama dengan pemetaan AppSetting di AppDbContext.
    /// </summary>
    private static Task EnsureAppSettingsTableAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS app_settings (" +
            "setting_key character varying(100) NOT NULL PRIMARY KEY, " +
            "value text NOT NULL, " +
            "updated_at timestamp without time zone NOT NULL, " +
            "updated_by character varying(254) NULL)", ct);

    /// <summary>
    /// Kolom tambahan untuk lokasi tersimpan dan notifikasi (titik pusat lokasi; jenis, tingkat, perkiraan waktu, dan jarak pada notifikasi).
    /// EnsureCreated tidak mengubah tabel yang sudah ada, jadi kolom ditambahkan di sini bila belum ada (sampai migration, TODO B4).
    /// Skema harus sama dengan pemetaan di AppDbContext. Nilai bawaan hanya untuk baris lama; baris baru selalu diisi aplikasi.
    /// </summary>
    private static async Task EnsureNotificationColumnsAsync(AppDbContext db, CancellationToken ct)
    {
        string[] statements =
        {
            "ALTER TABLE monitoring_areas ADD COLUMN IF NOT EXISTS latitude double precision NOT NULL DEFAULT 0",
            "ALTER TABLE monitoring_areas ADD COLUMN IF NOT EXISTS longitude double precision NOT NULL DEFAULT 0",
            "ALTER TABLE notifications ADD COLUMN IF NOT EXISTS area_id character varying(40) NULL REFERENCES monitoring_areas (area_id) ON DELETE CASCADE",
            "ALTER TABLE notifications ADD COLUMN IF NOT EXISTS kind character varying(10) NOT NULL DEFAULT 'Fire'",
            "ALTER TABLE notifications ADD COLUMN IF NOT EXISTS level integer NOT NULL DEFAULT 1",
            "ALTER TABLE notifications ADD COLUMN IF NOT EXISTS eta_hours integer NOT NULL DEFAULT 0",
            "ALTER TABLE notifications ADD COLUMN IF NOT EXISTS distance_km double precision NOT NULL DEFAULT 0",
            "CREATE INDEX IF NOT EXISTS \"IX_notifications_area_id\" ON notifications (area_id)",
            "CREATE INDEX IF NOT EXISTS \"IX_notifications_user_id_created_at\" ON notifications (user_id, created_at)"
        };
        foreach (var sql in statements) await db.Database.ExecuteSqlRawAsync(sql, ct);   // semua konstanta, bukan input pengguna
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
