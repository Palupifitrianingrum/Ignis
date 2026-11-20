using System.Security.Cryptography;
using Ignis.Api.Data;
using Ignis.Shared;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Api.Integrations;

/// <summary>
/// API key layanan luar yang bisa diganti admin dari aplikasi (override) di atas key bawaan dari .env/konfigurasi.
///
/// Prioritas key yang berlaku: <b>override admin &gt; .env &gt; kosong</b>. Override TIDAK mengubah atau menghapus key .env;
/// mengembalikan ke default (<see cref="ResetAsync"/>) hanya membuang override sehingga key .env berlaku lagi.
///
/// Keamanan: override disimpan terenkripsi (ASP.NET Data Protection) di tabel app_settings, tidak pernah dalam teks biasa;
/// key lengkap tidak pernah dikembalikan ke pemanggil (hanya versi bertopeng); key dites dulu sebelum disimpan.
/// Perubahan berlaku seketika, tanpa restart, karena klien membaca <see cref="IntegrationOptions"/> yang sama (singleton).
/// </summary>
public sealed class IntegrationKeyStore
{
    private sealed record ProviderDef(string Id, string Name, string SettingKey);

    private static readonly ProviderDef[] Providers =
    {
        new("firms", "NASA FIRMS", "integrations.firms.key"),
        new("weather", "OpenWeather", "integrations.weather.key")
    };

    private const string Purpose = "Ignis.IntegrationKeys.v1";
    public const int MinKeyLength = 8;
    public const int MaxKeyLength = 200;

    private readonly IServiceScopeFactory _scopes;
    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _http;
    private readonly IntegrationOptions _options;
    private readonly ILogger<IntegrationKeyStore>? _logger;

    // Key bawaan (dari .env/konfigurasi) dicatat sekali saat dibuat, sebelum ada override yang diterapkan.
    private readonly string _defaultFirms;
    private readonly string _defaultWeather;

    private readonly object _gate = new();
    private readonly HashSet<string> _unreadable = new();

    public IntegrationKeyStore(IServiceScopeFactory scopes, IDataProtectionProvider dataProtection, IHttpClientFactory http,
                               IntegrationOptions options, ILogger<IntegrationKeyStore>? logger = null)
    {
        _scopes = scopes;
        _protector = dataProtection.CreateProtector(Purpose);
        _http = http;
        _options = options;
        _logger = logger;
        _defaultFirms = options.FirmsKey?.Trim() ?? string.Empty;
        _defaultWeather = options.WeatherKey?.Trim() ?? string.Empty;
    }

    public static bool IsKnown(string provider) => Providers.Any(p => p.Id.Equals(provider, StringComparison.OrdinalIgnoreCase));

    private static ProviderDef Find(string provider) =>
        Providers.FirstOrDefault(p => p.Id.Equals(provider, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Layanan tidak dikenal: {provider}", nameof(provider));

    /// <summary>8 sampai 200 karakter tanpa spasi atau karakter kontrol. Bukan jaminan key benar (itu tugas uji koneksi).</summary>
    public static bool IsPlausibleKey(string? key) =>
        key is { Length: >= MinKeyLength and <= MaxKeyLength } && key.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c));

    /// <summary>Versi bertopeng untuk ditampilkan: empat karakter terakhir bila cukup panjang, selain itu tertutup penuh.</summary>
    public static string? Mask(string? key) =>
        string.IsNullOrEmpty(key) ? null : key.Length >= 12 ? "••••" + key[^4..] : "••••••••";

    // ------------------------------------------------------------------ menerapkan ke pengaturan yang dipakai klien

    private string EffectiveKey(ProviderDef def) => def.Id == "firms" ? _options.FirmsKey.Trim() : _options.EffectiveWeatherKey;

    private void Apply(ProviderDef def, string key)
    {
        if (def.Id == "firms") _options.FirmsKey = key;
        else _options.WeatherKey = key;
    }

    private string DefaultKey(ProviderDef def) => def.Id == "firms" ? _defaultFirms : _defaultWeather;

    // ------------------------------------------------------------------ memuat, membaca, mengubah

    /// <summary>Terapkan override tersimpan ke pengaturan. Dipanggil saat server start (setelah database siap).</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.AppSettings.AsNoTracking().ToListAsync(ct);

        foreach (var def in Providers)
        {
            var row = rows.FirstOrDefault(r => r.Key == def.SettingKey);
            if (row == null)
            {
                Apply(def, DefaultKey(def));
                continue;
            }

            try
            {
                Apply(def, _protector.Unprotect(row.Value));
            }
            catch (CryptographicException ex)
            {
                // Mis. kunci enkripsi server berubah/hilang. Jangan gagal start: pakai key bawaan dan beri tahu admin.
                lock (_gate) _unreadable.Add(def.Id);
                Apply(def, DefaultKey(def));
                _logger?.LogWarning(ex, "Override key {Provider} tidak dapat didekripsi; memakai key bawaan sampai diisi ulang.", def.Id);
            }
        }
    }

    public async Task<IReadOnlyList<IntegrationKeyDto>> GetAllAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.AppSettings.AsNoTracking().ToListAsync(ct);
        return Providers.Select(def => ToDto(def, rows.FirstOrDefault(r => r.Key == def.SettingKey))).ToList();
    }

    public async Task<IntegrationKeyDto> GetAsync(string provider, CancellationToken ct = default) =>
        (await GetAllAsync(ct)).Single(d => d.Provider.Equals(Find(provider).Id, StringComparison.OrdinalIgnoreCase));

    private IntegrationKeyDto ToDto(ProviderDef def, AppSetting? row)
    {
        bool unreadable;
        lock (_gate) unreadable = _unreadable.Contains(def.Id);

        var effective = EffectiveKey(def);
        var source = row != null && !unreadable ? KeySource.Override
                   : effective.Length > 0 ? KeySource.Environment
                   : KeySource.None;

        return new IntegrationKeyDto(
            def.Id, def.Name, source, Mask(effective), HasOverride: row != null,
            row?.UpdatedAt, row?.UpdatedBy,
            unreadable ? "Override tersimpan tidak dapat dibaca (kunci enkripsi server berubah). Key bawaan (.env) yang dipakai; isi ulang key atau kembalikan ke default." : null);
    }

    /// <summary>
    /// Simpan key sebagai override (terenkripsi) dan terapkan seketika. Pemanggil WAJIB sudah memvalidasi dan menguji key
    /// (lihat <see cref="TestAsync"/>); method ini hanya menyimpan.
    /// </summary>
    public async Task SetAsync(string provider, string key, string? changedBy, CancellationToken ct = default)
    {
        var def = Find(provider);
        key = key.Trim();
        if (!IsPlausibleKey(key)) throw new ArgumentException("Format key tidak valid.", nameof(key));

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AppSettings.FirstOrDefaultAsync(r => r.Key == def.SettingKey, ct);
        if (row == null) db.AppSettings.Add(row = new AppSetting { Key = def.SettingKey });
        row.Value = _protector.Protect(key);
        row.UpdatedAt = DateTime.Now;
        row.UpdatedBy = changedBy;
        await db.SaveChangesAsync(ct);

        lock (_gate) _unreadable.Remove(def.Id);
        Apply(def, key);
    }

    /// <summary>Buang override sehingga key bawaan (.env) berlaku lagi. Mengembalikan false bila memang tidak ada override.</summary>
    public async Task<bool> ResetAsync(string provider, CancellationToken ct = default)
    {
        var def = Find(provider);
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AppSettings.FirstOrDefaultAsync(r => r.Key == def.SettingKey, ct);
        if (row != null)
        {
            db.AppSettings.Remove(row);
            await db.SaveChangesAsync(ct);
        }

        lock (_gate) _unreadable.Remove(def.Id);
        Apply(def, DefaultKey(def));
        return row != null;
    }

    /// <summary>
    /// Uji koneksi dengan key kandidat (atau key yang sedang berlaku bila kandidat kosong) lewat satu permintaan kecil.
    /// Memakai salinan pengaturan, jadi tidak mengubah key yang sedang dipakai. Pesan hasilnya tidak memuat key.
    /// </summary>
    public async Task<ProviderCheck> TestAsync(string provider, string? candidateKey, CancellationToken ct = default)
    {
        var def = Find(provider);
        var key = string.IsNullOrWhiteSpace(candidateKey) ? EffectiveKey(def) : candidateKey.Trim();

        var temp = _options.Clone();
        if (def.Id == "firms") temp.FirmsKey = key;
        else temp.WeatherKey = key;

        if (def.Id == "firms")
            return await new FirmsClient(_http.CreateClient(nameof(FirmsClient)), temp).CheckConnectionAsync(ct);
        return await new WeatherClient(_http.CreateClient(nameof(WeatherClient)), temp).CheckConnectionAsync(ct);
    }

    public string NameOf(string provider) => Find(provider).Name;
}
