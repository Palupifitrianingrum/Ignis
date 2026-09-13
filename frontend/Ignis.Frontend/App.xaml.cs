using System.Configuration;
using System.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using Ignis.Frontend.Views.Auth;
using Ignis.Frontend.Navigation;
using Ignis.Frontend.ViewModels.Auth;
using Ignis.Frontend.ViewModels;

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

        base.OnStartup(e);

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpClient("IgnisApi", client =>
        {
            client.BaseAddress = new Uri("https://localhost:5001/api/");
        });

        services.AddSingleton<IDialogService, DialogService>();
        services.AddTransient<LoginFormViewModel>();
        services.AddTransient<LoginWindowViewModel>();
        services.AddTransient<MainWindowViewModel>();

        services.AddTransient<LoginView>();
        services.AddTransient<LoginFormView>();
        services.AddTransient<MainWindow>();
    }
}

