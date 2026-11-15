using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Ignis.Frontend.Services.Backend;

/// <summary>Hasil login Google di browser: kode sekali pakai yang akan ditukar server menjadi identitas pengguna.</summary>
public sealed record GoogleAuthCode(string Code, string CodeVerifier, string RedirectUri);

/// <summary>
/// Alur OAuth Google untuk aplikasi desktop (RFC 8252): buka browser sistem ke halaman izin Google, dengarkan
/// pengalihan di alamat loopback 127.0.0.1, ambil "code". PKCE (code_challenge) melindungi code dari pencurian.
/// Tidak ada rahasia di sini: penukaran code memakai client secret yang hanya ada di server.
/// </summary>
public static class GoogleSignIn
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    public static async Task<GoogleAuthCode> RunAsync(string clientId)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var redirectUri = $"http://127.0.0.1:{FreePort()}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var url = "https://accounts.google.com/o/oauth2/v2/auth"
                  + $"?client_id={Uri.EscapeDataString(clientId)}"
                  + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                  + "&response_type=code"
                  + $"&scope={Uri.EscapeDataString("openid email profile")}"
                  + $"&code_challenge={challenge}&code_challenge_method=S256"
                  + $"&state={state}&prompt=select_account";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        var pending = listener.GetContextAsync();
        if (await Task.WhenAny(pending, Task.Delay(Timeout)) != pending)
            throw new BackendException("Login Google tidak diselesaikan dalam 3 menit. Coba lagi.");

        var context = await pending;
        var query = context.Request.QueryString;
        await Respond(context, success: query["code"] != null && query["error"] == null);

        if (query["error"] != null) throw new BackendException("Login Google dibatalkan.");
        if (query["state"] != state || string.IsNullOrEmpty(query["code"])) throw new BackendException("Respons login Google tidak valid.");

        return new GoogleAuthCode(query["code"]!, verifier, redirectUri);
    }

    private static async Task Respond(HttpListenerContext context, bool success)
    {
        var html = success
            ? "<html><body style='font-family:sans-serif;text-align:center;margin-top:15%'><h2>Login berhasil</h2><p>Anda boleh menutup tab ini dan kembali ke aplikasi Ignis.</p></body></html>"
            : "<html><body style='font-family:sans-serif;text-align:center;margin-top:15%'><h2>Login dibatalkan</h2><p>Kembali ke aplikasi Ignis untuk mencoba lagi.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
