using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Ignis.Frontend.Navigation;
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
