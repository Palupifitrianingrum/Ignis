using Ignis.Core.Entities;

namespace Ignis.Core.Abstractions;

public interface IAuthService
{
    Task<User?> AuthenticateAsync(string email, string password, CancellationToken ct = default);
    Task<User?> RegisterAsync(string name, string email, string password, CancellationToken ct = default);
    Task LogoutAsync(string userId, CancellationToken ct = default);
}

public interface INotifier
{
    /// <summary>Hotspot baru yang berada di area pantauan pengguna.</summary>
    Task<IReadOnlyList<Hotspot>> CheckNewHotspotsAsync(string userId, DateTime since, CancellationToken ct = default);
    Task SendAsync(Notification notification, CancellationToken ct = default);
}

public interface IPredictionModel
{
    string ModelName { get; }
    string ModelVersion { get; }

    /// <summary>Hitung prediksi untuk satu hotspot berdasarkan cuaca. Id dan penyimpanan ditangani pemanggil.</summary>
    Prediction Predict(Hotspot hotspot, WeatherData weather);
}
