using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ignis.Api.Data;
using Ignis.Api.Integrations;
using Ignis.Api.Services;
using Ignis.Shared;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ignis.Tests;

/// <summary>Keamanan sesi: pencabutan token, refresh, ganti password, kunci login, rate limit, manajemen role, dan login Google.</summary>
public class AuthSecurityTests
{
    private sealed class Factory(bool rateLimit = false, int limit = 20, IGoogleIdTokenVerifier? verifier = null,
                                 Func<HttpRequestMessage, HttpResponseMessage>? google = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:Provider", "InMemory");
            builder.UseSetting("Storage:InMemoryName", "sec-" + Guid.NewGuid());
            builder.UseSetting("Jwt:Key", "kunci-rahasia-untuk-tes-minimal-32-karakter!");
            builder.UseSetting("Auth:RateLimitEnabled", rateLimit ? "true" : "false");
            builder.UseSetting("Auth:RateLimitPerMinute", limit.ToString());
            builder.UseSetting("Google:ClientId", "client-tes.apps.googleusercontent.com");
            builder.UseSetting("Google:ClientSecret", "");
            if (verifier != null || google != null)
                builder.ConfigureServices(s =>
                {
                    if (verifier != null) s.AddSingleton(verifier);
                    if (google != null)
                        s.AddHttpClient<GoogleOAuthClient>().ConfigurePrimaryHttpMessageHandler(() => new Fake(google));
                });
        }
    }

    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(f(r));
    }

    private sealed class FakeVerifier(GoogleIdentity id) : IGoogleIdTokenVerifier
    {
        public Task<GoogleIdentity> VerifyAsync(string idToken, string clientId, CancellationToken ct = default) => Task.FromResult(id);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private const string Pw = "password-aman";

    private static async Task<(HttpClient Client, AuthResponse Auth)> Login(WebApplicationFactory<Program> f, string email, string pw = DemoData.Password)
    {
        var c = f.CreateClient();
        var r = await c.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, pw));
        r.EnsureSuccessStatusCode();
        var a = (await r.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.Token);
        return (c, a);
    }

    private static async Task<(HttpClient Client, AuthResponse Auth)> Register(WebApplicationFactory<Program> f, string email)
    {
        var c = f.CreateClient();
        var r = await c.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Tes User", email, Pw));
        r.EnsureSuccessStatusCode();
        var a = (await r.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.Token);
        return (c, a);
    }

    private static HttpClient WithToken(WebApplicationFactory<Program> f, string token)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    // ---------------------------------------------------------------- sesi

    [Fact]
    public async Task Logout_RevokesToken()
    {
        await using var f = new Factory();
        var (c, a) = await Register(f, "logout@x.id");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken(f, a.Token).GetAsync("/api/auth/me")).StatusCode);
        var (c2, _) = await Login(f, "logout@x.id", Pw);
        Assert.Equal(HttpStatusCode.OK, (await c2.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Deactivate_RevokesTokenImmediately()
    {
        await using var f = new Factory();
        var (user, a) = await Register(f, "nonaktif@x.id");
        var (admin, _) = await Login(f, "admin@ignis.example");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/users/{a.User.Id}/toggle-active", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_GivesWorkingNewToken()
    {
        await using var f = new Factory();
        var (c, a) = await Register(f, "refresh@x.id");
        var r = await c.PostAsync("/api/auth/refresh", null);
        r.EnsureSuccessStatusCode();
        var n = (await r.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        Assert.Equal(HttpStatusCode.OK, (await WithToken(f, n.Token).GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_RequiresToken()
    {
        await using var f = new Factory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Refresh_BeyondMaxSession_IsDenied()
    {
        await using var f = new Factory();
        var (_, a) = await Register(f, "lama@x.id");
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AuthService>();
        await Assert.ThrowsAsync<AuthException>(() => svc.RefreshAsync(a.User.Id, DateTime.UtcNow.AddHours(-13)));
        var ok = await svc.RefreshAsync(a.User.Id, DateTime.UtcNow.AddHours(-2));
        Assert.NotEmpty(ok.Token);
    }

    // ---------------------------------------------------------------- ganti password

    [Fact]
    public async Task ChangePassword_RevokesOldTokens_AndNewPasswordWorks()
    {
        await using var f = new Factory();
        var (c, a) = await Register(f, "ganti@x.id");
        var r = await c.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(Pw, "password-baru-lebih-panjang"));
        r.EnsureSuccessStatusCode();
        var n = (await r.Content.ReadFromJsonAsync<AuthResponse>(Json))!;

        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken(f, a.Token).GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WithToken(f, n.Token).GetAsync("/api/auth/me")).StatusCode);
        await Login(f, "ganti@x.id", "password-baru-lebih-panjang");
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("ganti@x.id", Pw))).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_Is400_NotSessionExpiry()
    {
        await using var f = new Factory();
        var (c, _) = await Register(f, "salah@x.id");
        var r = await c.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("bukan-ini-woi", "password-baru-lebih-panjang"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("pendek")]
    [InlineData("password123")]
    [InlineData("aaaaaaaaaa")]
    public async Task ChangePassword_WeakNew_Is400(string weak)
    {
        await using var f = new Factory();
        var (c, _) = await Register(f, "lemah@x.id");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(Pw, weak))).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_SameAsCurrent_Is400()
    {
        await using var f = new Factory();
        var (c, _) = await Register(f, "sama@x.id");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(Pw, Pw))).StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_Is400()
    {
        await using var f = new Factory();
        var r = await f.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest("Budi", "budi@x.id", "12345678"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // ---------------------------------------------------------------- kunci login dan rate limit

    [Fact]
    public async Task Login_Lockout_After5Failures_Gives429WithRetryAfter_EvenForCorrectPassword()
    {
        await using var f = new Factory();
        await Register(f, "kunci@x.id");
        var c = f.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/auth/login", new LoginRequest("kunci@x.id", "salah-terus"))).StatusCode);

        var locked = await c.PostAsJsonAsync("/api/auth/login", new LoginRequest("kunci@x.id", Pw));
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.True(locked.Headers.TryGetValues("Retry-After", out var v) && int.Parse(v.First()) > 0);
    }

    [Fact]
    public async Task Login_Lockout_AlsoAppliesToUnknownEmail()
    {
        await using var f = new Factory();
        var c = f.CreateClient();
        for (var i = 0; i < 5; i++) await c.PostAsJsonAsync("/api/auth/login", new LoginRequest("hantu@x.id", "salah-terus"));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.PostAsJsonAsync("/api/auth/login", new LoginRequest("hantu@x.id", "salah-terus"))).StatusCode);
    }

    [Fact]
    public async Task RateLimiter_PerIp_Gives429()
    {
        await using var f = new Factory(rateLimit: true, limit: 3);
        var c = f.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) codes.Add((await c.PostAsJsonAsync("/api/auth/login", new LoginRequest($"u{i}@x.id", "salah-terus"))).StatusCode);
        Assert.Equal(3, codes.Count(x => x == HttpStatusCode.Unauthorized));
        Assert.Equal(2, codes.Count(x => x == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public void Throttle_UnlocksAfterTime_AndSuccessResets()
    {
        var clock = new Clock();
        var t = new LoginThrottle(new AuthOptions { MaxFailedAttempts = 3, LockoutMinutes = 5 }, clock);
        Assert.False(t.Failed("A"));
        Assert.False(t.Failed("a"));
        Assert.True(t.Failed(" A "));   // kunci dinormalkan
        Assert.NotNull(t.RetryAfter("a"));
        clock.Now += TimeSpan.FromMinutes(6);
        Assert.Null(t.RetryAfter("a"));

        t.Failed("b"); t.Failed("b"); t.Succeeded("b"); t.Failed("b");
        Assert.Null(t.RetryAfter("b"));   // sukses menghapus hitungan
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // ---------------------------------------------------------------- role

    [Fact]
    public async Task Role_Promote_GivesAdminAccess_AndRevokesOldToken()
    {
        await using var f = new Factory();
        var (user, a) = await Register(f, "naik@x.id");
        var (admin, _) = await Login(f, "admin@ignis.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/users")).StatusCode);

        var r = await admin.PutAsJsonAsync($"/api/users/{a.User.Id}/role", new SetUserRoleRequest(Role.Admin), Json);
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/users")).StatusCode);
        var (promoted, _) = await Login(f, "naik@x.id", Pw);
        Assert.Equal(HttpStatusCode.OK, (await promoted.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Role_Demote_RemovesAdminAccessImmediately()
    {
        await using var f = new Factory();
        var (_, a) = await Register(f, "turun@x.id");
        var (admin, _) = await Login(f, "admin@ignis.example");
        await admin.PutAsJsonAsync($"/api/users/{a.User.Id}/role", new SetUserRoleRequest(Role.Admin), Json);
        var (newAdmin, _) = await Login(f, "turun@x.id", Pw);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/users/{a.User.Id}/role", new SetUserRoleRequest(Role.Registered), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await newAdmin.GetAsync("/api/users")).StatusCode);
        var (back, _) = await Login(f, "turun@x.id", Pw);
        Assert.Equal(HttpStatusCode.Forbidden, (await back.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Role_Guards_Self_Unknown_Registered_LastAdmin()
    {
        await using var f = new Factory();
        var (admin, a) = await Login(f, "admin@ignis.example");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/users/{a.User.Id}/role", new SetUserRoleRequest(Role.Registered), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/users/TIDAK-ADA/role", new SetUserRoleRequest(Role.Admin), Json)).StatusCode);

        var (user, u) = await Register(f, "biasa@x.id");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync($"/api/users/{a.User.Id}/role", new SetUserRoleRequest(Role.Registered), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync($"/api/users/{u.User.Id}/role", new SetUserRoleRequest(Role.Admin), Json)).StatusCode);

        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserService>();
        var admins = (await users.GetAllAsync()).Where(x => x.Role == Role.Admin && x.IsActive).ToList();
        Assert.True(admins.Count >= 2);
        foreach (var other in admins.Skip(1)) Assert.Null(await users.SetRoleAsync("orang-lain", other.Id, Role.Registered));
        Assert.NotNull(await users.SetRoleAsync("orang-lain", admins[0].Id, Role.Registered));   // tinggal satu: ditolak
    }

    [Fact]
    public async Task Role_Promote_DeletesSavedLocations()
    {
        await using var f = new Factory();
        var (user, a) = await Register(f, "area@x.id");
        var add = await user.PostAsJsonAsync("/api/monitoring-areas", new MonitoringAreaRequest("Rumah", -2.9, 104.7, 10), Json);
        Assert.True(add.IsSuccessStatusCode, await add.Content.ReadAsStringAsync());
        var (admin, _) = await Login(f, "admin@ignis.example");
        await admin.PutAsJsonAsync($"/api/users/{a.User.Id}/role", new SetUserRoleRequest(Role.Admin), Json);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Ignis.Api.Data.AppDbContext>();
        Assert.Empty(db.MonitoringAreas.Where(x => x.UserId == a.User.Id));
    }

    // ---------------------------------------------------------------- Google

    private static Factory GoogleFactory(GoogleIdentity id, out List<string> bodies)
    {
        var list = new List<string>();
        bodies = list;
        return new Factory(verifier: new FakeVerifier(id), google: r =>
        {
            list.Add(r.Content!.ReadAsStringAsync().Result);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id_token\":\"abc\"}") };
        });
    }

    private static Task<HttpResponseMessage> GoogleLogin(HttpClient c) =>
        c.PostAsJsonAsync("/api/auth/google", new GoogleLoginRequest("kode", "verifier", "http://127.0.0.1:5555/"));

    [Fact]
    public async Task Google_NewUser_IsCreatedAsRegistered_WithoutPassword()
    {
        await using var f = GoogleFactory(new GoogleIdentity("sub-1", "baru@gmail.com", "Baru"), out var bodies);
        var r = await GoogleLogin(f.CreateClient());
        r.EnsureSuccessStatusCode();
        var a = (await r.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        Assert.Equal(Role.Registered, a.User.Role);
        Assert.False(a.User.HasPassword);
        Assert.True(a.User.GoogleLinked);
        Assert.DoesNotContain("client_secret", bodies[0]);
        Assert.Contains("code_verifier=verifier", bodies[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("baru@gmail.com", ""))).StatusCode);
    }

    [Fact]
    public async Task Google_SameSubject_ReturnsSameAccount()
    {
        await using var f = GoogleFactory(new GoogleIdentity("sub-2", "satu@gmail.com", "Satu"), out _);
        var first = (await (await GoogleLogin(f.CreateClient())).Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        var second = (await (await GoogleLogin(f.CreateClient())).Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        Assert.Equal(first.User.Id, second.User.Id);
    }

    [Fact]
    public async Task Google_LinksExistingEmail_AndClearsPreHijackPassword()
    {
        await using var f = GoogleFactory(new GoogleIdentity("sub-3", "korban@gmail.com", "Korban"), out _);
        var (attacker, old) = await Register(f, "korban@gmail.com");
        var g = await GoogleLogin(f.CreateClient());
        g.EnsureSuccessStatusCode();
        var ga = (await g.Content.ReadFromJsonAsync<AuthResponse>(Json))!;

        Assert.Equal(old.User.Id, ga.User.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("korban@gmail.com", Pw))).StatusCode);
    }

    [Fact]
    public async Task Google_LinkingAdmin_KeepsPassword()
    {
        await using var f = GoogleFactory(new GoogleIdentity("sub-4", "admin@ignis.example", "Admin"), out _);
        var g = await GoogleLogin(f.CreateClient());
        g.EnsureSuccessStatusCode();
        Assert.Equal(Role.Admin, (await g.Content.ReadFromJsonAsync<AuthResponse>(Json))!.User.Role);
        await Login(f, "admin@ignis.example");
    }

    [Fact]
    public async Task Google_DeactivatedAccount_IsRejected()
    {
        await using var f = GoogleFactory(new GoogleIdentity("sub-5", "siti.rahma@example.com", "Siti"), out _);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GoogleLogin(f.CreateClient())).StatusCode);
    }

    [Fact]
    public async Task Google_ProviderRejects_GivesReadableMessage()
    {
        await using var f = new Factory(google: _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent("{\"error\":\"invalid_client\",\"error_description\":\"The OAuth client was not found.\"}") });
        var r = await GoogleLogin(f.CreateClient());
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("Google__ClientId", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Google_Config_ExposesClientId_NotSecret()
    {
        await using var f = new Factory();
        var body = await f.CreateClient().GetStringAsync("/api/auth/google/config");
        Assert.Contains("client-tes", body);
        Assert.DoesNotContain("ecret", body);
    }

    [Theory]
    [InlineData("{\"error\":\"invalid_grant\"}", "tidak berlaku")]
    [InlineData("{\"error\":\"redirect_uri_mismatch\"}", "Desktop app")]
    [InlineData("{\"error\":\"x\",\"error_description\":\"client_secret is missing.\"}", "ClientSecret")]
    [InlineData("bukan json", "menolak")]
    public void ExplainError_Maps(string body, string expected) =>
        Assert.Contains(expected, GoogleOAuthClient.ExplainError(body));

    // ---------------------------------------------------------------- matriks otorisasi

    [Fact]
    public async Task EveryApiRoute_IsEitherWhitelistedAnonymous_OrRequiresAuthorization()
    {
        await using var f = new Factory();
        _ = f.CreateClient();
        var sources = f.Services.GetServices<Microsoft.AspNetCore.Routing.EndpointDataSource>();
        var anonymousAllowed = new HashSet<string> { "/api/auth/register", "/api/auth/login", "/api/auth/google", "/api/auth/google/config" };
        var seen = 0;
        foreach (var e in sources.SelectMany(s => s.Endpoints).OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>())
        {
            var path = e.RoutePattern.RawText ?? "";
            if (!path.StartsWith("/api")) continue;
            seen++;
            var hasAuth = e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>() != null;
            var anon = e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() != null;
            if (anonymousAllowed.Contains(path)) Assert.True(anon, path);
            else Assert.True(hasAuth && !anon, "Route tanpa otorisasi: " + path);
        }
        Assert.True(seen > 10);
        await Task.CompletedTask;
    }
}
