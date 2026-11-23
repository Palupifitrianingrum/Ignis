namespace Ignis.Api.Domain;

/// <summary>Pelanggaran aturan domain (input/keadaan tidak valid). Di API dipetakan ke HTTP 400.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

/// <summary>Pemeriksaan invarian yang dipakai entity. Dipusatkan di sini agar pesan konsisten.</summary>
internal static class Guard
{
    public static void Location(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude < -90 || latitude > 90)
            throw new DomainException("Latitude harus berupa angka antara -90 dan 90.");
        if (double.IsNaN(longitude) || longitude < -180 || longitude > 180)
            throw new DomainException("Longitude harus berupa angka antara -180 dan 180.");
    }

    public static void Confidence(double confidence)
    {
        if (double.IsNaN(confidence) || confidence < 0 || confidence > 100)
            throw new DomainException("Tingkat keyakinan harus antara 0 dan 100.");
    }

    public static void NotBlank(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException(message);
    }
}
