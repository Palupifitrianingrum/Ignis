namespace Ignis.Api.Support;

/// <summary>
/// Muat berkas .env ke environment. Dicari di folder kerja, folder aplikasi, lalu folder-folder induknya,
/// sehingga .env di root repo terbaca walau API dijalankan dari folder mana pun. Nilai yang sudah ada di environment tidak ditimpa.
/// </summary>
public static class DotEnv
{
    public static void Load(string fileName)
    {
        // Jangan muat .env saat dijalankan dari test runner (xUnit) agar tes tetap hermetik dan memakai InMemory.
        if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) == true))
            return;

        var path = Find(fileName, Directory.GetCurrentDirectory()) ?? Find(fileName, AppContext.BaseDirectory);
        if (path == null) return;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#')) continue;
            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = parts[0].Trim();
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, parts[1].Trim().Trim('"'));
        }
    }

    private static string? Find(string fileName, string startDir)
    {
        for (var dir = new DirectoryInfo(startDir); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
