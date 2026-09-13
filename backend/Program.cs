using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static readonly HttpClient client = new HttpClient();
    static string apiKey = string.Empty;

    static async Task Main(string[] args)
    {
        LoadEnv(".env");
        apiKey = Environment.GetEnvironmentVariable("OPENWEATHER_API_KEY") ?? string.Empty;

        if (string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine("API Key tidak ditemukan. Pastikan file .env ada dan berisi OPENWEATHER_API_KEY.");
            return;
        }

        string city = "Yogyakarta";
        string countryCode = "ID";

        await GetCurrentWeather(city, countryCode);
    }

    static void LoadEnv(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"File .env tidak ditemukan di: {Path.GetFullPath(path)}");
            return;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();

            // skip baris kosong atau komentar
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length != 2) continue;

            string key = parts[0].Trim();
            string value = parts[1].Trim().Trim('"'); // buang tanda kutip kalau ada

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    static async Task GetCurrentWeather(string city, string countryCode)
    {
        string url = $"https://api.openweathermap.org/data/2.5/weather?q={city},{countryCode}&appid={apiKey}&units=metric&lang=id";

        var response = await client.GetAsync(url);
        string content = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            using JsonDocument doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            string namaKota = root.GetProperty("name").GetString() ?? city;
            double suhu = root.GetProperty("main").GetProperty("temp").GetDouble();
            double terasaSeperti = root.GetProperty("main").GetProperty("feels_like").GetDouble();
            string deskripsi = root.GetProperty("weather")[0].GetProperty("description").GetString() ?? "-";
            int kelembapan = root.GetProperty("main").GetProperty("humidity").GetInt32();

            Console.WriteLine($"Cuaca di {namaKota}:");
            Console.WriteLine($"Suhu: {suhu}°C (terasa seperti {terasaSeperti}°C)");
            Console.WriteLine($"Kondisi: {deskripsi}");
            Console.WriteLine($"Kelembapan: {kelembapan}%");
        }
        else
        {
            Console.WriteLine($"Gagal ambil data: {response.StatusCode}");
            Console.WriteLine(content);
        }
    }
}