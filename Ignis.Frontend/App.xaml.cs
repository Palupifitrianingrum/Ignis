using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Ignis.Core.Abstractions;
using Ignis.Core.InMemory;
using Ignis.Core.Services;
using Ignis.Integrations;
using Ignis.Frontend.Navigation;
using Ignis.Frontend.Services.Admin;
using Ignis.Frontend.Services.Backend;
using Ignis.Frontend.ViewModels;
using Ignis.Frontend.ViewModels.Auth;
using Ignis.Frontend.Views.Admin;
using Ignis.Frontend.Views.Auth;

namespace Ignis.Frontend;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Pengaman: exception dari handler async (mis. backend tidak terjangkau) tampil sebagai pesan, bukan membuat aplikasi crash.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Terjadi kesalahan", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        base.OnStartup(e);

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // ---- Ignis.Core: penyimpanan (mode demo/in-memory) ----
        // TODO(backend): ganti dengan implementasi yang memanggil Ignis.Api (HTTP) setelah server siap.
        services.AddSingleton<IHotspotRepository>(_ => new InMemoryHotspotRepository(DemoData.Hotspots()));
        services.AddSingleton<IPredictionRepository>(_ => new InMemoryPredictionRepository(DemoData.Predictions()));
        services.AddSingleton<IUserRepository>(_ => new InMemoryUserRepository(DemoData.Users()));
        services.AddSingleton<IActivityLog>(_ => new InMemoryActivityLog(DemoData.Activities()));

        // ---- Integrasi eksternal (transisi: desktop masih memanggil NASA FIRMS/OpenWeather langsung) ----
        // TODO(backend): pindahkan sinkronisasi ke server (BackgroundService di Ignis.Api).
        services.AddSingleton<AppSettings>(_ => AppSettings.Load());
        services.AddSingleton<ISettingsProvider>(sp => sp.GetRequiredService<AppSettings>());
        services.AddHttpClient<IFirmsClient, FirmsClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<OpenWeatherClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IWeatherProvider>(sp =>
            new FallbackWeatherProvider(sp.GetRequiredService<OpenWeatherClient>(), new SimulatedWeatherProvider()));

        // ---- Ignis.Core: service (use case) ----
        services.AddSingleton<IPredictionModel, FwiPredictionModel>();
        services.AddSingleton<HotspotService>();
        services.AddSingleton<PredictionService>();
        services.AddSingleton<UserService>();
        services.AddSingleton<HotspotSyncService>();

        // ---- Backend untuk halaman admin: API (PostgreSQL) atau demo, sesuai AppSettings.BackendMode ----
        services.AddSingleton<LocalAdminBackend>();
        services.AddHttpClient<ApiAdminBackend>((sp, c) =>
        {
            var url = sp.GetRequiredService<AppSettings>().ApiBaseUrl.Trim();
            c.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
            c.Timeout = TimeSpan.FromSeconds(60);   // sync/prediksi bisa lebih lama dari panggilan biasa
        });
        services.AddSingleton<IAdminBackend>(sp => sp.GetRequiredService<AppSettings>().UseApi
            ? sp.GetRequiredService<ApiAdminBackend>()
            : sp.GetRequiredService<LocalAdminBackend>());
        services.AddSingleton<AdminWorkspace>();

        services.AddSingleton<IDialogService, DialogService>();
        services.AddTransient<LoginFormViewModel>();
        services.AddTransient<RegisterViewModel>();
        services.AddTransient<LoginWindowViewModel>();
        services.AddTransient<MainWindowViewModel>();

        services.AddTransient<LoginView>();
        services.AddTransient<RegisterView>();
        services.AddTransient<LoginFormView>();
        services.AddTransient<MainWindow>();
        services.AddTransient<AdminMainWindow>();
    }
}
