using System.Text.Json.Serialization;
using Ignis.Api;
using Ignis.Core.Abstractions;
using Ignis.Core.Common;
using Ignis.Core.Contracts;
using Ignis.Core.Enums;
using Ignis.Core.InMemory;
using Ignis.Core.Services;
using Ignis.Infrastructure;
using Ignis.Infrastructure.Persistence;
using Ignis.Integrations;

// Muat .env (OPENWEATHER_API_KEY, dll) ke environment sebelum konfigurasi dibaca — perilaku yang sama dengan console app lama.
DotEnv.Load(".env");

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---- Penyimpanan (InMemory/Postgres lewat Storage:Provider) ----
builder.Services.AddIgnisStorage(builder.Configuration);

// ---- Integrasi eksternal: konfigurasi dari appsettings/env, bukan dari klien ----
builder.Services.AddSingleton<ISettingsProvider, ConfigSettingsProvider>();
builder.Services.AddHttpClient<IFirmsClient, FirmsClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient<OpenWeatherClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<IWeatherProvider>(sp =>
    new FallbackWeatherProvider(sp.GetRequiredService<OpenWeatherClient>(), new SimulatedWeatherProvider()));

// ---- Service Core (scoped: repository EF bersifat scoped) ----
builder.Services.AddSingleton<IPredictionModel, FwiPredictionModel>();
builder.Services.AddScoped<HotspotService>();
builder.Services.AddScoped<PredictionService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<HotspotSyncService>();

// Sinkronisasi terjadwal di server (mati secara default: Sync:Enabled=false). Lihat SyncBackgroundService.
builder.Services.AddHostedService<SyncBackgroundService>();

var app = builder.Build();

// Buat tabel + (opsional) isi data demo saat memakai PostgreSQL.
await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);

// Pelanggaran aturan domain (DomainException) -> HTTP 400 dengan pesan yang bisa langsung ditampilkan klien.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (DomainException ex)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

// TODO(backend): autentikasi JWT + otorisasi. Sampai itu ada, SEMUA endpoint terbuka — jangan diekspos ke jaringan publik.
//   app.UseAuthentication(); app.UseAuthorization();
//   lalu: api.RequireAuthorization(p => p.RequireRole("Admin")) untuk endpoint tulis (User.CanManageHotspots).
//   Endpoint auth: POST /api/auth/login, /api/auth/register (butuh IAuthService — lihat Ignis.Infrastructure/Services/AuthService.cs)

app.MapGet("/health", () => Results.Ok(new { status = "ok", storage = app.Configuration["Storage:Provider"] ?? "InMemory" }));

var api = app.MapGroup("/api");

// ---------- Hotspot ----------
api.MapGet("/hotspots", async (HotspotService svc, HotspotSource? source, HotspotStatus? status) =>
{
    var all = await svc.GetAllAsync();
    return Results.Ok(HotspotFilter.Apply(all, null, source, status)
        .OrderByDescending(h => h.DetectedAt).Select(h => h.ToDto()).ToList());
});

api.MapGet("/hotspots/{id}", async (string id, HotspotService svc) =>
    await svc.GetByIdAsync(id) is { } h ? Results.Ok(h.ToDto()) : Results.NotFound());

// Fitur utama tugas OOP: tambah hotspot manual (lihat HotspotService.AddManualAsync + hierarki Hotspot).
api.MapPost("/hotspots/manual", async (ManualHotspotRequest req, HotspotService svc) =>
{
    var saved = await svc.AddManualAsync(req);
    return Results.Created($"/api/hotspots/{saved.Id}", saved.ToDto());
});

api.MapPut("/hotspots/{id}", async (string id, ManualHotspotRequest req, HotspotService svc) =>
    Results.Ok((await svc.UpdateManualAsync(id, req)).ToDto()));

api.MapDelete("/hotspots/{id}", async (string id, HotspotService svc) =>
    await svc.DeleteAsync(id) ? Results.NoContent() : Results.NotFound());

// ---------- Cuaca & prediksi ----------
api.MapGet("/hotspots/{id}/weather", async (string id, HotspotService hs, PredictionService svc) =>
    await hs.GetByIdAsync(id) is { } h ? Results.Ok((await svc.GetWeatherAsync(h)).ToDto()) : Results.NotFound());

api.MapGet("/predictions", async (PredictionService svc) =>
    Results.Ok((await svc.GetAllAsync()).Select(p => p.ToDto()).ToList()));

api.MapGet("/hotspots/{id}/prediction", async (string id, HotspotService hs, PredictionService svc) =>
    await hs.GetByIdAsync(id) is { } h ? Results.Ok((await svc.GetOrPredictAsync(h)).ToDto()) : Results.NotFound());

api.MapPost("/hotspots/{id}/predictions", async (string id, HotspotService hs, PredictionService svc) =>
    await hs.GetByIdAsync(id) is { } h ? Results.Ok((await svc.PredictAsync(h)).ToDto()) : Results.NotFound());

// ---------- Pengguna ----------
api.MapGet("/users", async (UserService svc) =>
    Results.Ok((await svc.GetAllAsync()).Select(u => u.ToDto()).ToList()));   // tanpa PasswordHash

api.MapPost("/users/{id}/toggle-active", async (string id, UserService svc) =>
    await svc.ToggleActiveAsync(id) is { } error ? Results.BadRequest(new { error }) : Results.NoContent());

// ---------- Log aktivitas & sinkronisasi ----------
api.MapGet("/activity", async (IActivityLog log) => Results.Ok(await log.RecentAsync()));

api.MapPost("/activity", async (ActivityRequest req, IActivityLog log) =>
{
    if (string.IsNullOrWhiteSpace(req.Message)) return Results.BadRequest(new { error = "Pesan aktivitas wajib diisi." });
    await log.AddAsync(req.Message.Trim());
    return Results.NoContent();
});

api.MapPost("/sync", async (HotspotSyncService sync) => Results.Ok(await sync.RunAsync()));

// ---------- Simulasi penyebaran api ----------
// TODO(backend): GET /api/hotspots/{id}/simulation?hours=N -> IEnumerable<SimulationFrame>
//   menunggu implementasi IFireSpreadSimulator (Ignis.Core/Simulation/CellularAutomataSimulator.cs).

// ---------- Notifikasi & area pantauan ----------
// TODO(backend): /api/notifications, /api/monitoring-areas menunggu INotifier & repository area pantauan.

app.Run();
