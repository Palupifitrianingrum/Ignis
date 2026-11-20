using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ignis.Api.Data;
using Ignis.Api.Integrations;
using Ignis.Shared;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Ignis.Tests;

// Override API key dari aplikasi: prioritas override > .env > kosong, terenkripsi, diuji dulu, key lengkap tidak pernah dikembalikan.
// Semua dengan HTTP palsu (tanpa key sungguhan dan tanpa internet).

/// <summary>IHttpClientFactory palsu: semua klien memakai satu <see cref="FakeHandler"/>.</summary>
public sealed class FakeHttpClientFactory(FakeHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

public class IntegrationKeyStoreTests
{
    private const string DefaultFirms = "KUNCI-BAWAAN-FIRMS-0001";
    private const string NewFirms = "KUNCI-BARU-FIRMS-A3F9";

    private sealed class Rig
    {
        public required IntegrationOptions Options { get; init; }
        public required FakeHandler Handler { get; init; }
        public required IntegrationKeyStore Store { get; init; }
        public required ServiceProvider Services { get; init; }
        public required string DbName { get; init; }
        public required IDataProtectionProvider Dp { get; init; }

        public async Task<AppSetting?> RowAsync(string key)
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AppSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Key == key);
        }

        public static Rig Create(string? defaultFirms = null, string? defaultWeather = null, string? dbName = null, IDataProtectionProvider? dp = null)
        {
            dbName ??= "kunci-" + Guid.NewGuid();
            dp ??= new EphemeralDataProtectionProvider();
            var options = new IntegrationOptions { FirmsKey = defaultFirms ?? "", WeatherKey = defaultWeather ?? "" };
            var handler = new FakeHandler();
            var services = new ServiceCollection()
                .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName))
                .BuildServiceProvider();
            var store = new IntegrationKeyStore(services.GetRequiredService<IServiceScopeFactory>(), dp, new FakeHttpClientFactory(handler), options);
            return new Rig { Options = options, Handler = handler, Store = store, Services = services, DbName = dbName, Dp = dp };
        }
    }

    [Fact]
    public async Task NoDefaultNoOverride_IsNone()
    {
        var rig = Rig.Create();
        var firms = await rig.Store.GetAsync("firms");

        Assert.Equal(KeySource.None, firms.Source);
        Assert.Null(firms.MaskedKey);
        Assert.False(firms.HasOverride);
        Assert.Null(firms.Warning);
    }

    [Fact]
    public async Task EnvKeyOnly_IsEnvironment_AndMasked()
    {
        var rig = Rig.Create(defaultFirms: DefaultFirms);
        var firms = await rig.Store.GetAsync("firms");

        Assert.Equal(KeySource.Environment, firms.Source);
        Assert.Equal("••••0001", firms.MaskedKey);
        Assert.False(firms.HasOverride);
    }

    [Fact]
    public async Task Override_AppliesImmediately_AndIsEncryptedAtRest()
    {
        var rig = Rig.Create(defaultFirms: DefaultFirms);

        await rig.Store.SetAsync("firms", NewFirms, "admin@ignis.example");

        Assert.Equal(NewFirms, rig.Options.FirmsKey);   // klien membaca opsi yang sama: berlaku tanpa restart
        var dto = await rig.Store.GetAsync("firms");
        Assert.Equal(KeySource.Override, dto.Source);
        Assert.True(dto.HasOverride);
        Assert.Equal("••••A3F9", dto.MaskedKey);
        Assert.Equal("admin@ignis.example", dto.UpdatedBy);
        Assert.NotNull(dto.UpdatedAt);

        var row = await rig.RowAsync("integrations.firms.key");
        Assert.NotNull(row);
        Assert.DoesNotContain(NewFirms, row!.Value);        // tidak pernah teks biasa di database
        Assert.DoesNotContain("A3F9", row.Value);
    }

    [Fact]
    public async Task Priority_OverrideBeatsEnv_AndResetBringsEnvBack()
    {
        var rig = Rig.Create(defaultFirms: DefaultFirms);
        await rig.Store.SetAsync("firms", NewFirms, "a");

        Assert.True(await rig.Store.ResetAsync("firms"));

        Assert.Equal(DefaultFirms, rig.Options.FirmsKey);   // key .env tidak pernah hilang
        var dto = await rig.Store.GetAsync("firms");
        Assert.Equal(KeySource.Environment, dto.Source);
        Assert.False(dto.HasOverride);
        Assert.Null(await rig.RowAsync("integrations.firms.key"));
    }

    [Fact]
    public async Task Reset_WithoutOverride_IsANoOp()
    {
        var rig = Rig.Create(defaultFirms: DefaultFirms);

        Assert.False(await rig.Store.ResetAsync("firms"));

        Assert.Equal(DefaultFirms, rig.Options.FirmsKey);
    }

    [Fact]
    public async Task Reset_WithNoEnvKey_LeavesNothing()
    {
        var rig = Rig.Create();
        await rig.Store.SetAsync("firms", NewFirms, "a");

        await rig.Store.ResetAsync("firms");

        Assert.Equal(string.Empty, rig.Options.FirmsKey);
        Assert.Equal(KeySource.None, (await rig.Store.GetAsync("firms")).Source);
    }

    [Fact]
    public async Task Providers_AreIndependent()
    {
        var rig = Rig.Create(defaultFirms: DefaultFirms, defaultWeather: "KUNCI-CUACA-BAWAAN-9999");

        await rig.Store.SetAsync("weather", "KUNCI-CUACA-BARU-7777", "a");

        Assert.Equal(DefaultFirms, rig.Options.FirmsKey);
        Assert.Equal("KUNCI-CUACA-BARU-7777", rig.Options.WeatherKey);
        Assert.Equal(KeySource.Environment, (await rig.Store.GetAsync("firms")).Source);
        Assert.Equal(KeySource.Override, (await rig.Store.GetAsync("weather")).Source);
    }

    [Fact]
    public async Task OverrideSurvivesARestart()
    {
        var first = Rig.Create(defaultFirms: DefaultFirms);
        await first.Store.SetAsync("firms", NewFirms, "a");

        // "restart": proses baru, database dan kunci enkripsi sama, opsi kembali ke nilai .env
        var second = Rig.Create(defaultFirms: DefaultFirms, dbName: first.DbName, dp: first.Dp);
        Assert.Equal(DefaultFirms, second.Options.FirmsKey);
        await second.Store.LoadAsync();

        Assert.Equal(NewFirms, second.Options.FirmsKey);
        Assert.Equal(KeySource.Override, (await second.Store.GetAsync("firms")).Source);
    }

    [Fact]
    public async Task UnreadableOverride_FallsBackToEnv_WarnsAdmin_AndCanBeReset()
    {
        var first = Rig.Create(defaultFirms: DefaultFirms);
        await first.Store.SetAsync("firms", NewFirms, "a");

        // kunci enkripsi berbeda (mis. key ring hilang saat container diganti)
        var second = Rig.Create(defaultFirms: DefaultFirms, dbName: first.DbName, dp: new EphemeralDataProtectionProvider());
        await second.Store.LoadAsync();   // tidak boleh melempar

        Assert.Equal(DefaultFirms, second.Options.FirmsKey);
        var dto = await second.Store.GetAsync("firms");
        Assert.Equal(KeySource.Environment, dto.Source);
        Assert.True(dto.HasOverride);   // barisnya masih ada, jadi tombol reset tetap relevan
        Assert.Contains("tidak dapat dibaca", dto.Warning);

        await second.Store.ResetAsync("firms");
        Assert.Null((await second.Store.GetAsync("firms")).Warning);
    }

    [Fact]
    public async Task OverwritingAnOverride_ReplacesIt()
    {
        var rig = Rig.Create();
        await rig.Store.SetAsync("firms", "PERTAMA-KUNCI-1111", "a");
        await rig.Store.SetAsync("firms", "KEDUA-KUNCI-2222", "b");

        Assert.Equal("KEDUA-KUNCI-2222", rig.Options.FirmsKey);
        var dto = await rig.Store.GetAsync("firms");
        Assert.Equal("b", dto.UpdatedBy);
        Assert.Equal("••••2222", dto.MaskedKey);
    }

    [Theory]
    [InlineData("1234567890ab", "••••90ab")]     // 12+: empat terakhir
    [InlineData("abcdefghijklmnopqrstuvwxyz012345", "••••2345")]
    [InlineData("12345678901", "••••••••")]      // < 12: tertutup penuh
    [InlineData("abc", "••••••••")]
    public void Mask_ShowsOnlyTheLastFour_WhenLongEnough(string key, string expected) =>
        Assert.Equal(expected, IntegrationKeyStore.Mask(key));

    [Fact]
    public void Mask_EmptyIsNull()
    {
        Assert.Null(IntegrationKeyStore.Mask(""));
        Assert.Null(IntegrationKeyStore.Mask(null));
    }

    [Theory]
    [InlineData("12345678", true)]
    [InlineData("abcdef0123456789abcdef0123456789", true)]
    [InlineData("1234567", false)]          // terlalu pendek
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("ada spasi di sini", false)]
    [InlineData("12345678\n", false)]       // karakter kontrol
    [InlineData("12345678\t90", false)]
    public void IsPlausibleKey(string? key, bool expected) => Assert.Equal(expected, IntegrationKeyStore.IsPlausibleKey(key));

    [Fact]
    public void IsPlausibleKey_RejectsOver200Chars()
    {
        Assert.True(IntegrationKeyStore.IsPlausibleKey(new string('a', 200)));
        Assert.False(IntegrationKeyStore.IsPlausibleKey(new string('a', 201)));
    }

    [Fact]
    public async Task Set_RejectsAnImplausibleKey_AndStoresNothing()
    {
        var rig = Rig.Create(defaultFirms: DefaultFirms);

        await Assert.ThrowsAsync<ArgumentException>(() => rig.Store.SetAsync("firms", "pendek", "a"));

        Assert.Equal(DefaultFirms, rig.Options.FirmsKey);
        Assert.Null(await rig.RowAsync("integrations.firms.key"));
    }

    [Fact]
    public void IsKnown_AcceptsOnlyTheTwoProviders_CaseInsensitive()
    {
        Assert.True(IntegrationKeyStore.IsKnown("firms"));
        Assert.True(IntegrationKeyStore.IsKnown("WEATHER"));
        Assert.False(IntegrationKeyStore.IsKnown("google"));
        Assert.False(IntegrationKeyStore.IsKnown(""));
    }

    [Fact]
    public async Task Test_UsesTheCandidate_WithoutChangingTheKeyInUse()
    {
        var rig = Rig.Create(defaultFirms: DefaultFirms);
        rig.Handler.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n");

        var check = await rig.Store.TestAsync("firms", NewFirms);

        Assert.Equal(ConnectionState.Connected, check.State);
        var url = Assert.Single(rig.Handler.Urls);
        Assert.Contains(NewFirms, url);
        Assert.DoesNotContain(DefaultFirms, url);
        Assert.Equal(DefaultFirms, rig.Options.FirmsKey);   // tidak berubah oleh pengujian
    }

    [Fact]
    public async Task Test_WithoutCandidate_UsesTheEffectiveKey()
    {
        var rig = Rig.Create(defaultFirms: DefaultFirms);
        rig.Handler.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n");

        await rig.Store.TestAsync("firms", null);

        Assert.Contains(DefaultFirms, Assert.Single(rig.Handler.Urls));
    }

    [Fact]
    public async Task Test_FailureMessage_NeverContainsTheCandidate()
    {
        var rig = Rig.Create();
        rig.Handler.Enqueue(HttpStatusCode.BadRequest, "Invalid MAP_KEY.");

        var check = await rig.Store.TestAsync("firms", NewFirms);

        Assert.Equal(ConnectionState.Failed, check.State);
        Assert.DoesNotContain(NewFirms, check.Message);
    }

    [Fact]
    public async Task Test_Weather_UsesTheCandidate()
    {
        var rig = Rig.Create(defaultWeather: "KUNCI-CUACA-BAWAAN-9999");
        rig.Handler.Enqueue(HttpStatusCode.OK, Samples.WeatherJson, "application/json");

        var check = await rig.Store.TestAsync("weather", "KUNCI-CUACA-BARU-7777");

        Assert.Equal(ConnectionState.Connected, check.State);
        var url = Assert.Single(rig.Handler.Urls);
        Assert.Contains("appid=KUNCI-CUACA-BARU-7777", url);
        Assert.Equal("KUNCI-CUACA-BAWAAN-9999", rig.Options.WeatherKey);
    }

    [Fact]
    public async Task Client_PicksUpTheNewKeyImmediately_BecauseItSharesTheOptions()
    {
        var rig = Rig.Create(defaultFirms: DefaultFirms);
        var client = new FirmsClient(new HttpClient(rig.Handler), rig.Options);   // klien yang sama dengan yang dipakai sinkronisasi
        rig.Handler.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n");

        await rig.Store.SetAsync("firms", NewFirms, "a");
        await client.FetchCsvAsync("S1");

        Assert.Contains(NewFirms, Assert.Single(rig.Handler.Urls));
    }
}

public class IntegrationKeyApiTests : IClassFixture<IntegrationKeyApiTests.Factory>
{
    private const string Key = "KUNCI-BARU-FIRMS-A3F9";

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public FakeHandler Handler { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:Provider", "InMemory");
            builder.UseSetting("Storage:InMemoryName", "kunci-api-" + Guid.NewGuid());
            builder.UseSetting("Jwt:Key", "kunci-rahasia-untuk-tes-minimal-32-karakter!");
            builder.UseSetting("Auth:RateLimitEnabled", "false");
            builder.ConfigureServices(services =>
            {
                // Hermetik: tanpa key dari .env, tanpa internet, kunci enkripsi sementara.
                services.RemoveAll<IntegrationOptions>();
                services.AddSingleton(new IntegrationOptions());
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(Handler));
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            });
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly Factory _factory;
    public IntegrationKeyApiTests(Factory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, DemoData.Password));
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    /// <summary>Kembali ke keadaan awal sebelum tiap tes (kelas ini berbagi satu server).</summary>
    private async Task<HttpClient> AdminAsync()
    {
        var admin = await LoginAsync("admin@ignis.example");
        foreach (var p in new[] { "firms", "weather" }) await admin.DeleteAsync($"/api/settings/integrations/{p}");
        _factory.Handler.Reset();
        _factory.Handler.Urls.Clear();
        return admin;
    }

    private static async Task<IntegrationKeyDto> GetKeyAsync(HttpClient c, string provider) =>
        (await c.GetFromJsonAsync<List<IntegrationKeyDto>>("/api/settings/integrations", Json))!.Single(k => k.Provider == provider);

    private static Task<HttpResponseMessage> PutAsync(HttpClient c, string provider, string key) =>
        c.PutAsJsonAsync($"/api/settings/integrations/{provider}", new SetIntegrationKeyRequest(key));

    [Fact]
    public async Task WithoutToken_Is401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/settings/integrations")).StatusCode);

    [Fact]
    public async Task RegularUser_Is403_ForEveryOperation()
    {
        var user = await LoginAsync("andi.pratama@example.com");
        _factory.Handler.Reset();
        _factory.Handler.Urls.Clear();   // handler dipakai bersama antar-tes dalam kelas ini

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/settings/integrations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(user, "firms", Key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync("/api/settings/integrations/firms")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/settings/integrations/firms/test", new TestIntegrationKeyRequest(Key))).StatusCode);
        Assert.Equal(0, _factory.Handler.Calls);   // ditolak sebelum menyentuh layanan luar
    }

    [Fact]
    public async Task Admin_SeesBothProviders_WithoutAnyKey()
    {
        var admin = await AdminAsync();
        var list = (await admin.GetFromJsonAsync<List<IntegrationKeyDto>>("/api/settings/integrations", Json))!;

        Assert.Equal(new[] { "firms", "weather" }, list.Select(k => k.Provider).ToArray());
        var firms = list[0];
        Assert.Equal(KeySource.None, firms.Source);
        Assert.Null(firms.MaskedKey);
        Assert.False(firms.HasOverride);
    }

    [Fact]
    public async Task Put_ValidKey_IsTestedThenSaved_AndNeverEchoedBack()
    {
        var admin = await AdminAsync();
        _factory.Handler.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n");

        var resp = await PutAsync(admin, "firms", Key);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Key, body);   // key lengkap tidak pernah dikembalikan
        var dto = JsonSerializer.Deserialize<IntegrationKeyDto>(body, Json)!;
        Assert.Equal(KeySource.Override, dto.Source);
        Assert.Equal("••••A3F9", dto.MaskedKey);
        Assert.Equal("admin@ignis.example", dto.UpdatedBy);

        Assert.Contains(Key, Assert.Single(_factory.Handler.Urls));   // diuji dengan key kandidat itu

        var again = await GetKeyAsync(admin, "firms");
        Assert.Equal(KeySource.Override, again.Source);
        Assert.True(again.HasOverride);
    }

    [Fact]
    public async Task Put_KeyThatFailsTheTest_IsRejected_AndNothingChanges()
    {
        var admin = await AdminAsync();
        _factory.Handler.Enqueue(HttpStatusCode.BadRequest, "Invalid MAP_KEY.");

        var resp = await PutAsync(admin, "firms", Key);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Contains("tidak disimpan", text);
        Assert.DoesNotContain(Key, text);
        Assert.Equal(KeySource.None, (await GetKeyAsync(admin, "firms")).Source);
    }

    [Fact]
    public async Task Put_FailedKey_DoesNotReplaceAWorkingOverride()
    {
        var admin = await AdminAsync();
        _factory.Handler.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n");
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, "firms", "KUNCI-YANG-JALAN-1234")).StatusCode);

        _factory.Handler.Reset().Enqueue(HttpStatusCode.BadRequest, "Invalid MAP_KEY.");
        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(admin, "firms", "KUNCI-SALAH-5678-ABCD")).StatusCode);

        var dto = await GetKeyAsync(admin, "firms");
        Assert.Equal(KeySource.Override, dto.Source);
        Assert.Equal("••••1234", dto.MaskedKey);   // masih key yang jalan
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pendek")]
    [InlineData("ada spasi di tengah key ini")]
    public async Task Put_ImplausibleKey_Is400_WithoutCallingTheProvider(string key)
    {
        var admin = await AdminAsync();

        var resp = await PutAsync(admin, "firms", key);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("Format key tidak valid", await resp.Content.ReadAsStringAsync());
        Assert.Equal(0, _factory.Handler.Calls);
    }

    [Fact]
    public async Task UnknownProvider_Is404()
    {
        var admin = await AdminAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(admin, "google", Key)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/settings/integrations/google")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/settings/integrations/google/test", new TestIntegrationKeyRequest(Key))).StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesTheOverride()
    {
        var admin = await AdminAsync();
        _factory.Handler.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n");
        await PutAsync(admin, "firms", Key);

        var resp = await admin.DeleteAsync("/api/settings/integrations/firms");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = (await resp.Content.ReadFromJsonAsync<IntegrationKeyDto>(Json))!;
        Assert.Equal(KeySource.None, dto.Source);   // tanpa key .env pada tes ini
        Assert.False(dto.HasOverride);
    }

    [Fact]
    public async Task TestEndpoint_ChecksACandidate_WithoutSavingIt()
    {
        var admin = await AdminAsync();
        _factory.Handler.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n");

        var resp = await admin.PostAsJsonAsync("/api/settings/integrations/firms/test", new TestIntegrationKeyRequest(Key));

        var status = (await resp.Content.ReadFromJsonAsync<ProviderStatusDto>(Json))!;
        Assert.Equal(ConnectionState.Connected, status.State);
        Assert.DoesNotContain(Key, status.Message);
        Assert.Equal(KeySource.None, (await GetKeyAsync(admin, "firms")).Source);   // tidak tersimpan
    }

    [Fact]
    public async Task TestEndpoint_WithoutKeyAndWithoutCandidate_ReportsMissingKey()
    {
        var admin = await AdminAsync();

        var resp = await admin.PostAsJsonAsync("/api/settings/integrations/firms/test", new TestIntegrationKeyRequest(null));

        var status = (await resp.Content.ReadFromJsonAsync<ProviderStatusDto>(Json))!;
        Assert.Equal(ConnectionState.MissingKey, status.State);
        Assert.Equal(0, _factory.Handler.Calls);
    }

    [Fact]
    public async Task NewKey_IsUsedByTheStatusCheck_AndByTheSync_WithoutRestart()
    {
        var admin = await AdminAsync();
        _factory.Handler.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n");
        await PutAsync(admin, "firms", Key);
        _factory.Handler.Urls.Clear();
        _factory.Handler.Reset().Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n");

        await admin.GetAsync("/api/integrations/status");
        await admin.PostAsync("/api/sync", null);

        Assert.All(_factory.Handler.Urls.Where(u => u.Contains("firms.modaps")), u => Assert.Contains(Key, u));
        Assert.Contains(_factory.Handler.Urls, u => u.Contains("firms.modaps") && u.Contains(Key));
    }

    [Fact]
    public async Task Changes_AreRecordedInTheActivityLog_WithoutTheKey()
    {
        var admin = await AdminAsync();
        _factory.Handler.Enqueue(HttpStatusCode.OK, Samples.ViirsHeader + "\n");
        await PutAsync(admin, "firms", Key);
        await admin.DeleteAsync("/api/settings/integrations/firms");

        var log = await admin.GetStringAsync("/api/activity");

        Assert.Contains("mengganti key NASA FIRMS", log);
        Assert.Contains("mengembalikan key NASA FIRMS ke default", log);
        Assert.Contains("admin@ignis.example", log);
        Assert.DoesNotContain(Key, log);
    }
}
