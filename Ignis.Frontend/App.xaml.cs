using System;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Ignis.Frontend.Navigation;
using Ignis.Frontend.Services.Admin;
using Ignis.Frontend.Services.Backend;
using Ignis.Frontend.ViewModels;
using Ignis.Frontend.ViewModels.Auth;
using Ignis.Frontend.Views.Admin;
using Ignis.Frontend.Views.Auth;
using Ignis.Frontend.Views.Dashboard;

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
        // ---- Pengaturan lokal + klien API ----
        // IgnisApiClient harus singleton: ia menyimpan sesi login (token JWT) yang dipakai semua halaman.
        services.AddSingleton<AppSettings>(_ => AppSettings.Load());
        services.AddSingleton<IgnisApiClient>(sp =>
        {
            var url = (Environment.GetEnvironmentVariable("IGNIS_API_URL") ?? sp.GetRequiredService<AppSettings>().ApiBaseUrl).Trim();
            return new IgnisApiClient(new HttpClient
            {
                BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/"),
                Timeout = TimeSpan.FromSeconds(60)   // sync/prediksi bisa lebih lama dari panggilan biasa
            });
        });
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
        services.AddTransient<UserSettingsWindow>();
    }
}
