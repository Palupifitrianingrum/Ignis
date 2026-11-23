namespace Ignis.Api.Endpoints;

/// <summary>Nama kebijakan otorisasi. "Admin" = token dengan role Admin; "Registered" = pengguna terdaftar (pemilik lokasi dan notifikasi).</summary>
public static class Policies
{
    public const string Admin = "Admin";
    public const string Registered = "Registered";
}
