using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ignis.Api.Data;
using Ignis.Shared;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ignis.Tests;

/// <summary>Tes end-to-end lewat pipeline HTTP sungguhan (JWT, otorisasi, penanganan error) dengan database InMemory.</summary>
public class AuthApiTests : IClassFixture<AuthApiTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Storage:Provider", "InMemory");
            builder.UseSetting("Storage:InMemoryName", "api-tes-" + Guid.NewGuid());
            builder.UseSetting("Jwt:Key", "kunci-rahasia-untuk-tes-minimal-32-karakter!");
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly Factory _factory;
    public AuthApiTests(Factory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsync(string email, string password = DemoData.Password)
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    private static ManualHotspotRequest NewHotspot() =>
        new(-2.9, 104.6, 80, DateTime.Now, "Tes", null, HotspotStatus.Active);

    [Fact]
    public async Task Health_IsPublic() =>
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().GetAsync("/health")).StatusCode);

    [Fact]
    public async Task Endpoints_RequireToken()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/hotspots")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Gives401_WithSameMessageAsUnknownEmail()
    {
        var c = _factory.CreateClient();
        var wrong = await c.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin@ignis.example", "salah-banget"));
        var unknown = await c.PostAsJsonAsync("/api/auth/login", new LoginRequest("tidak.ada@x.id", "apa-saja-1"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_DeactivatedUser_IsRejected()
    {
        var resp = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("siti.rahma@example.com", DemoData.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);   // Siti dinonaktifkan di data demo
    }

    [Fact]
    public async Task Admin_CanReadAndWrite()
    {
        var admin = await LoginAsync("admin@ignis.example");

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/users")).StatusCode);
        var created = await admin.PostAsJsonAsync("/api/hotspots/manual", NewHotspot(), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.StartsWith("HS-", (await created.Content.ReadFromJsonAsync<HotspotDto>(Json))!.Id);
    }

    [Fact]
    public async Task RegisteredUser_CanRead_ButNotWrite()
    {
        var user = await LoginAsync("andi.pratama@example.com");

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/hotspots")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/hotspots/manual", NewHotspot(), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync("/api/hotspots/HS-0138")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/sync", null)).StatusCode);
    }

    [Fact]
    public async Task Register_CreatesRegisteredUser_NeverAdmin()
    {
        var c = _factory.CreateClient();
        var resp = await c.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Pengguna Baru", "baru@x.id", "password-aman"));
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>(Json))!;

        Assert.Equal(Role.Registered, auth.User.Role);
        Assert.StartsWith("U-", auth.User.Id);

        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Register_RejectsShortPassword_AndDuplicateEmail()
    {
        var c = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/register", new RegisterRequest("A", "a1@x.id", "pendek"))).StatusCode);

        var first = await c.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Dua", "dua@x.id", "password-aman"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Dua", "DUA@x.id", "password-aman"))).StatusCode);
    }

    [Fact]
    public async Task Tampered_Token_IsRejected()
    {
        var c = await LoginAsync("admin@ignis.example");
        var token = c.DefaultRequestHeaders.Authorization!.Parameter!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^3] + (token.EndsWith("AAA") ? "BBB" : "AAA"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/hotspots")).StatusCode);
    }

    [Fact]
    public async Task Google_NotConfigured_ReportsNoClientId_AndRejectsLogin()
    {
        var c = _factory.CreateClient();
        var cfg = (await c.GetFromJsonAsync<GoogleConfigDto>("/api/auth/google/config", Json))!;
        Assert.Null(cfg.ClientId);

        var resp = await c.PostAsJsonAsync("/api/auth/google", new GoogleLoginRequest("kode", "verifier", "http://127.0.0.1:1/"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
