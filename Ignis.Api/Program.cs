using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Ignis.Api.Data;
using Ignis.Api.Endpoints;
using Ignis.Api.Integrations;
using Ignis.Api.Services;
using Ignis.Api.Support;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Titik masuk API. File ini hanya merangkai (wiring): daftar layanan, middleware, dan route.
// Logika ada di Services/, route di Endpoints/, database di Data/, panggilan ke layanan luar di Integrations/.

// Muat .env (OPENWEATHER_API_KEY, Jwt__Key, dll) ke environment sebelum konfigurasi dibaca.
DotEnv.Load(".env");

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---- Konfigurasi (dibaca sekali saat start) ----
var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key))
{
    // Tanpa Jwt:Key tetap bisa jalan untuk pengembangan, tetapi semua token hangus tiap server restart.
    jwt.Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    Console.WriteLine("PERINGATAN: Jwt:Key belum diisi. Memakai kunci acak sementara (isi Jwt__Key di .env untuk server sungguhan).");
}
else if (jwt.Key.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key minimal 32 karakter.");
}
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(config.GetSection("Integrations").Get<IntegrationOptions>() ?? new IntegrationOptions());
builder.Services.AddSingleton(config.GetSection("Google").Get<GoogleOptions>() ?? new GoogleOptions());
var authOptions = config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<IGoogleIdTokenVerifier, GoogleLibraryVerifier>();
builder.Services.AddSingleton(config.GetSection("Notifications").Get<NotificationOptions>() ?? new NotificationOptions());

// ---- Database: Supabase (Supabase:DbUrl terisi), PostgreSQL lokal (Storage:Provider=Postgres), atau InMemory untuk dev/test (default) ----
var database = DatabaseTarget.Resolve(config);
builder.Services.AddSingleton(database);
if (database.Provider == "Postgres")
{
    var cs = database.ConnectionString
             ?? throw new InvalidOperationException("ConnectionStrings:Ignis belum diisi untuk Storage:Provider=Postgres (atau isi Supabase__DbUrl).");
    builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cs));
}
else
{
    var name = config["Storage:InMemoryName"] ?? "ignis";
    builder.Services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(name));
}

// ---- Autentikasi (JWT) dan otorisasi ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;   // pakai nama klaim asli di token: "sub", "role", "name"
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = TokenService.SigningKey(jwt),
        NameClaimType = TokenService.NameClaim,
        RoleClaimType = TokenService.RoleClaim,
        ClockSkew = TimeSpan.FromMinutes(1),
    };

    // Token yang tanda tangan dan masa berlakunya sah belum tentu masih boleh dipakai: periksa akun di database.
    // Akun dihapus/dinonaktifkan, role berubah, atau nomor versi token naik (logout, ganti password) = token ditolak (401).
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            var principal = ctx.Principal!;
            var id = principal.FindFirstValue("sub");
            _ = int.TryParse(principal.FindFirstValue(TokenService.VersionClaim), out var version);
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var current = id == null ? null : await db.Users.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.IsActive, x.Role, x.TokenVersion }).FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);

            if (current == null || !current.IsActive || current.TokenVersion != version
                || current.Role.ToString() != principal.FindFirstValue(TokenService.RoleClaim))
                ctx.Fail("Sesi tidak berlaku lagi.");
        }
    };
});

// Pembatas laju per alamat IP untuk endpoint rawan tebak (login, registrasi, Google, ganti password). Lapisan pertama; kunci per-akun ada di LoginThrottle.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "60";
        await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Terlalu banyak permintaan. Coba lagi sebentar lagi." }, ct);
    };
    o.AddPolicy("auth", http => authOptions.RateLimitEnabled
        ? RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, authOptions.RateLimitPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
        : RateLimitPartition.GetNoLimiter("off"));
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(Ignis.Shared.Role.Admin)))
    .AddPolicy(Policies.Registered, p => p.RequireRole(nameof(Ignis.Shared.Role.Registered)));

// ---- Layanan aplikasi ----
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<FwiPredictionModel>();
builder.Services.AddSingleton<FireSpreadSimulator>();
builder.Services.AddSingleton<IntegrationStatus>();   // penjaga satu-sinkronisasi dan status cuaca terakhir
builder.Services.AddSingleton<IntegrationKeyStore>();   // override API key dari aplikasi (terenkripsi), di atas key .env

// Kunci enkripsi override key. Bawaan: Windows menyimpannya di profil pengguna (bertahan antar-restart). Di Docker/Linux set
// DataProtection__KeysPath ke folder yang disimpan (volume), kalau tidak override tidak bisa dibaca lagi setelah container diganti.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Ignis");
if (!string.IsNullOrWhiteSpace(config["DataProtection:KeysPath"]))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(config["DataProtection:KeysPath"]!));
// Timeout pendek: sinkronisasi dipicu dari desktop (batas 60 detik). User-Agent generik, tanpa data pengguna.
builder.Services.AddHttpClient<FirmsClient>(c => { c.Timeout = TimeSpan.FromSeconds(25); c.DefaultRequestHeaders.UserAgent.ParseAdd("Ignis-Api/1.0"); });
builder.Services.AddHttpClient<WeatherClient>(c => { c.Timeout = TimeSpan.FromSeconds(15); c.DefaultRequestHeaders.UserAgent.ParseAdd("Ignis-Api/1.0"); });
builder.Services.AddHttpClient<GoogleOAuthClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<ActivityLogService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<HotspotService>();
builder.Services.AddScoped<PredictionService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<MonitoringAreaService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddHostedService<SyncBackgroundService>();   // mati kecuali Sync:Enabled=true

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services, config);
await app.Services.GetRequiredService<IntegrationKeyStore>().LoadAsync();   // terapkan override key tersimpan

app.UseDomainErrors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", storage = database.IsSupabase ? "Supabase" : database.Provider }));
app.MapAuthEndpoints();
app.MapHotspotEndpoints();
app.MapAdminEndpoints();
app.MapSettingsEndpoints();
app.MapNotificationEndpoints();

app.Run();

/// <summary>Agar proyek tes dapat memakai WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
