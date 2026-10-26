using System.Windows;
using System.Windows.Threading;
namespace IgnisAdmin;

public partial class App : Application
{
    static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IgnisAdmin", "error.log");

    public App()
    {
        // Error di UI thread: tampilkan pesan, catat ke log, aplikasi tetap hidup bila memungkinkan
        DispatcherUnhandledException += (_, e) => { Report("UI", e.Exception); e.Handled = true; };
        // Error di thread lain / task: catat saja
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report("Fatal", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Report("Task", e.Exception); e.SetObserved(); };
    }

    static void Report(string kind, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}: {ex}\n\n");
        }
        catch { }
        MessageBox.Show($"Terjadi kesalahan ({kind}):\n\n{ex?.Message}\n\nDetail tersimpan di:\n{LogPath}", "Ignis Admin", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
