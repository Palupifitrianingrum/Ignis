using System.Windows;
using System.Windows.Media;

namespace Ignis.Frontend.Services.Admin;

public static class Ui
{
    public static Brush StatusBrush(string s)
    {
        var key = s == "Terhubung" ? "Green" : (s is "Belum dicek" or "Menyinkronkan..." or "API key belum diisi") ? "Muted" : "Red";
        return (Brush)Application.Current.FindResource(key);
    }
}
