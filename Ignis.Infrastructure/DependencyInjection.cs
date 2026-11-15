using Ignis.Core.Abstractions;
using Ignis.Core.InMemory;
using Ignis.Infrastructure.Persistence;
using Ignis.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ignis.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Daftarkan penyimpanan sesuai konfigurasi:
    ///  Storage:Provider = "InMemory" (default, data demo) atau "Postgres" (ConnectionStrings:Ignis).
    /// </summary>
    public static IServiceCollection AddIgnisStorage(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Storage:Provider"] ?? "InMemory";

        if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            var cs = config.GetConnectionString("Ignis")
                     ?? throw new InvalidOperationException("ConnectionStrings:Ignis belum diisi untuk Storage:Provider=Postgres.");
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cs));
            services.AddScoped<IHotspotRepository, EfHotspotRepository>();
            services.AddScoped<IWeatherRepository, EfWeatherRepository>();
            services.AddScoped<IPredictionRepository, EfPredictionRepository>();
            services.AddScoped<IUserRepository, EfUserRepository>();
            services.AddScoped<IActivityLog, EfActivityLog>();
        }
        else
        {
            // Mode demo: data contoh di memori (hilang saat proses berhenti).
            services.AddSingleton<IHotspotRepository>(_ => new InMemoryHotspotRepository(DemoData.Hotspots()));
            services.AddSingleton<IWeatherRepository, InMemoryWeatherRepository>();
            services.AddSingleton<IPredictionRepository>(_ => new InMemoryPredictionRepository(DemoData.Predictions()));
            services.AddSingleton<IUserRepository>(_ => new InMemoryUserRepository(DemoData.Users()));
            services.AddSingleton<IActivityLog>(_ => new InMemoryActivityLog(DemoData.Activities()));
        }

        // TODO(backend): daftarkan AuthService/NotificationService setelah diimplementasikan.
        // services.AddScoped<IAuthService, AuthService>();
        // services.AddScoped<INotifier, NotificationService>();
        return services;
    }
}
