using System.Text;
using Ignis.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ignis.Infrastructure.Persistence;

/// <summary>Baris log aktivitas (tabel activity_log). Tabel TAMBAHAN di luar ERD, untuk fitur "Aktivitas" di halaman pengguna.</summary>
public class ActivityLogEntry
{
    public long Id { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Pemetaan entity ke skema PostgreSQL sesuai ERD:
///   users ← admins, registered_users            (pewarisan tabel: PK sekaligus FK user_id)
///   hotspots ← satellite_hotspots, manual_hotspots (pewarisan tabel: PK sekaligus FK hotspot_id)
///   monitoring_areas, notifications, weather_data, predictions
/// Nama tabel dan kolom memakai snake_case (ERD: userId → user_id).
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Hotspot> Hotspots => Set<Hotspot>();
    public DbSet<WeatherData> WeatherData => Set<WeatherData>();
    public DbSet<Prediction> Predictions => Set<Prediction>();
    public DbSet<MonitoringArea> MonitoringAreas => Set<MonitoringArea>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ActivityLogEntry> ActivityLog => Set<ActivityLogEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder cb)
    {
        // Aplikasi memakai waktu lokal (DateTime.Now / Kind Unspecified). Npgsql menolak itu untuk "timestamp with time zone",
        // jadi disimpan sebagai "timestamp without time zone".
        // TODO(backend): bila nanti butuh multi-zona waktu, simpan UTC (DateTime.UtcNow) dan kembali ke timestamptz.
        cb.Properties<DateTime>().HaveColumnType("timestamp without time zone");
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ---------------- USER / ADMIN / REGISTERED_USER ----------------
        b.Entity<User>(e =>
        {
            e.UseTptMappingStrategy();
            e.ToTable("users", t => t.HasCheckConstraint("ck_users_role", "role IN ('Admin','Registered')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("user_id").HasMaxLength(40);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(254).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(12);
            e.HasIndex(x => x.Email).IsUnique();
        });
        b.Entity<Admin>().ToTable("admins");
        b.Entity<RegisteredUser>().ToTable("registered_users");

        // ---------------- MONITORING_AREA (dibuat REGISTERED_USER) ----------------
        b.Entity<MonitoringArea>(e =>
        {
            e.ToTable("monitoring_areas", t =>
            {
                t.HasCheckConstraint("ck_monitoring_areas_lat", "min_latitude <= max_latitude AND min_latitude >= -90 AND max_latitude <= 90");
                t.HasCheckConstraint("ck_monitoring_areas_lon", "min_longitude <= max_longitude AND min_longitude >= -180 AND max_longitude <= 180");
                t.HasCheckConstraint("ck_monitoring_areas_radius", "radius >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("area_id").HasMaxLength(40);
            e.Property(x => x.UserId).HasMaxLength(40);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasOne<RegisteredUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
        });

        // ---------------- HOTSPOT / SATELLITE_HOTSPOT / MANUAL_HOTSPOT ----------------
        b.Entity<Hotspot>(e =>
        {
            e.UseTptMappingStrategy();
            e.ToTable("hotspots", t =>
            {
                t.HasCheckConstraint("ck_hotspots_lat", "latitude BETWEEN -90 AND 90");
                t.HasCheckConstraint("ck_hotspots_lon", "longitude BETWEEN -180 AND 180");
                t.HasCheckConstraint("ck_hotspots_confidence", "confidence BETWEEN 0 AND 100");
                t.HasCheckConstraint("ck_hotspots_source", "source IN ('Satellite','Manual')");
                t.HasCheckConstraint("ck_hotspots_status", "status IN ('Active','Monitoring','Resolved')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("hotspot_id").HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.HasIndex(x => x.DetectedAt);
            // Deduplikasi impor FIRMS di level database. Untuk satelit, detected_at = waktu scan.
            e.HasIndex(x => new { x.DetectedAt, x.Latitude, x.Longitude })
                .IsUnique()
                .HasFilter("source = 'Satellite'");
        });
        b.Entity<SatelliteHotspot>(e =>
        {
            e.ToTable("satellite_hotspots");
            e.Property(x => x.SatelliteName).HasMaxLength(60).IsRequired();
            e.Property(x => x.Instrument).HasMaxLength(60).IsRequired();
        });
        b.Entity<ManualHotspot>(e =>
        {
            e.ToTable("manual_hotspots");
            e.Property(x => x.ReportedBy).HasMaxLength(120).IsRequired();
            e.Property(x => x.Note).HasMaxLength(1000);   // kolom TAMBAHAN di luar ERD (catatan laporan di form admin)
        });

        // ---------------- WEATHER_DATA (milik HOTSPOT) ----------------
        b.Entity<WeatherData>(e =>
        {
            e.ToTable("weather_data");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("weather_id").HasMaxLength(40);
            e.Property(x => x.HotspotId).HasMaxLength(20);
            e.Property(x => x.Condition).HasColumnName("weather_condition").HasMaxLength(120);
            e.HasOne<Hotspot>().WithMany().HasForeignKey(x => x.HotspotId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.HotspotId, x.Timestamp });
        });

        // ---------------- PREDICTION (berdasarkan HOTSPOT, memakai WEATHER_DATA) ----------------
        b.Entity<Prediction>(e =>
        {
            e.ToTable("predictions", t => t.HasCheckConstraint("ck_predictions_risk", "risk_level IN ('Low','Medium','High','Critical')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("prediction_id").HasMaxLength(20);
            e.Property(x => x.HotspotId).HasMaxLength(20);
            e.Property(x => x.WeatherId).HasMaxLength(40);
            e.Property(x => x.Direction).HasColumnName("spread_direction");
            e.Property(x => x.SpeedKmH).HasColumnName("spread_speed");
            e.Property(x => x.AreaHa).HasColumnName("estimated_area");
            e.Property(x => x.Hours).HasColumnName("estimated_time");
            e.Property(x => x.Risk).HasColumnName("risk_level").HasConversion<string>().HasMaxLength(10);
            e.HasOne<Hotspot>().WithMany().HasForeignKey(x => x.HotspotId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<WeatherData>().WithMany().HasForeignKey(x => x.WeatherId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.HotspotId, x.CreatedAt });
        });

        // ---------------- NOTIFICATION (diterima REGISTERED_USER, merujuk HOTSPOT) ----------------
        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("notification_id").HasMaxLength(40);
            e.Property(x => x.UserId).HasMaxLength(40);
            e.Property(x => x.HotspotId).HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Message).IsRequired();
            e.HasOne<RegisteredUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Hotspot>().WithMany().HasForeignKey(x => x.HotspotId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.IsRead });
        });

        // ---------------- ACTIVITY_LOG (tambahan, di luar ERD) ----------------
        b.Entity<ActivityLogEntry>(e =>
        {
            e.ToTable("activity_log");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CreatedAt);
        });

        ApplySnakeCaseColumns(b);
    }

    /// <summary>Kolom yang belum diberi nama eksplisit diubah ke snake_case (ERD: wind_speed, created_at, dst.).</summary>
    private static void ApplySnakeCaseColumns(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var p in entity.GetDeclaredProperties())
                if (p.FindAnnotation(RelationalAnnotationNames.ColumnName) is null)
                    p.SetColumnName(ToSnake(p.Name));
    }

    private static string ToSnake(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
