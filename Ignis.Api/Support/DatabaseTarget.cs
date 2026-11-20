using Npgsql;

namespace Ignis.Api.Support;

/// <summary>
/// Memilih database yang dipakai API. Urutan:
///  0. <c>Storage:Provider=InMemory</c> eksplisit: InMemory (mengalahkan semuanya).
///  1. <c>Supabase:DbUrl</c> (env <c>Supabase__DbUrl</c>) terisi: PostgreSQL di Supabase.
///  2. Kosong: <c>Storage:Provider</c> = Postgres memakai <c>ConnectionStrings:Ignis</c> (PostgreSQL lokal/Docker), selain itu InMemory.
/// Jadi pindah kembali ke PostgreSQL lokal cukup dengan mengosongkan <c>Supabase__DbUrl</c>.
///
/// Nilai Supabase boleh berupa URI (<c>postgresql://user:password@host:5432/postgres</c>, persis seperti di dasbor Supabase)
/// atau connection string Npgsql biasa. SSL diwajibkan.
/// </summary>
public sealed record DatabaseTarget(string Provider, string? ConnectionString, bool IsSupabase)
{
    public static DatabaseTarget Resolve(IConfiguration config)
    {
        var provider = config["Storage:Provider"] ?? "InMemory";

        // InMemory yang disebut eksplisit selalu menang (dipakai tes dan demo cepat), walau Supabase terisi.
        var supabase = config["Supabase:DbUrl"] ?? config["Supabase_DbUrl"];   // salah ketik satu garis bawah yang umum juga diterima
        if (!string.IsNullOrWhiteSpace(supabase) && !provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(supabase) && config["Storage:Provider"] == null))
            return new DatabaseTarget("Postgres", NormalizeSupabase(supabase), true);

        return provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
            ? new DatabaseTarget("Postgres", config.GetConnectionString("Ignis"), false)
            : new DatabaseTarget("InMemory", null, false);
    }

    /// <summary>Ubah URI/connection string Supabase menjadi connection string Npgsql dengan SSL.</summary>
    public static string NormalizeSupabase(string value)
    {
        value = value.Trim();
        var builder = new NpgsqlConnectionStringBuilder();

        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
                throw new InvalidOperationException("Supabase__DbUrl bukan URI PostgreSQL yang valid. Jika password memuat karakter khusus (@ : / ? #), ubah password di Supabase atau encode-persen.");

            var parts = uri.UserInfo.Split(':', 2);
            builder.Host = uri.Host;
            builder.Port = uri.Port > 0 ? uri.Port : 5432;
            builder.Username = Uri.UnescapeDataString(parts[0]);
            builder.Password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : null;
            builder.Database = uri.AbsolutePath.Trim('/') is { Length: > 0 } db ? Uri.UnescapeDataString(db) : "postgres";
        }
        else
        {
            builder.ConnectionString = value;
        }

        builder.SslMode = SslMode.Require;
        builder.TrustServerCertificate = true;   // sertifikat Supabase bukan dari CA publik; koneksi tetap terenkripsi
        return builder.ConnectionString;
    }
}
