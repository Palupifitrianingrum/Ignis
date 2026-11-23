using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ignis.Frontend.Services.Backend;
using Ignis.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Ignis.Frontend.Views.Admin.Controls;

/// <summary>
/// Mengatur API key satu layanan luar (FIRMS atau OpenWeather) yang disimpan di SERVER. Prioritas key yang berlaku:
/// override dari aplikasi &gt; file .env server &gt; kosong. Mengisi key baru menggantikan key yang berlaku (key .env tidak diubah);
/// kolom kosong tidak mengubah apa pun; "Kembalikan ke default" membuang override. Key diuji dulu oleh server sebelum disimpan,
/// dan key lengkap tidak pernah dikirim balik ke sini (hanya versi bertopeng).
/// </summary>
public partial class IntegrationKeyEditor : UserControl
{
    private static readonly Brush Ok = new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x4A));
    private static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xC2, 0x3B, 0x14));
    private static readonly Brush Neutral = new SolidColorBrush(Color.FromRgb(0x4A, 0x61, 0x5C));

    private IgnisApiClient? _api;
    private IntegrationKeyDto? _current;
    private bool _busy;

    /// <summary>"firms" atau "weather" (diisi dari XAML).</summary>
    public string Provider { get; set; } = string.Empty;

    public string Title
    {
        get => TxtTitle.Text;
        set => TxtTitle.Text = value;
    }

    public IntegrationKeyEditor()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DesignerProperties_IsInDesignMode() || App.Services == null) return;
            _api = App.Services.GetRequiredService<IgnisApiClient>();
            await RefreshAsync();
        };
    }

    private static bool DesignerProperties_IsInDesignMode() =>
        System.ComponentModel.DesignerProperties.GetIsInDesignMode(new DependencyObject());

    // ------------------------------------------------------------------ tampilan

    public async Task RefreshAsync()
    {
        if (_api == null) return;
        try
        {
            _current = (await _api.GetIntegrationKeysAsync()).FirstOrDefault(k => k.Provider == Provider);
            ShowCurrent();
        }
        catch (BackendException ex)
        {
            TxtSource.Text = "Tidak dapat membaca status key dari server.";
            TxtDetail.Text = ex.Message;
            BtnReset.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowCurrent()
    {
        var k = _current;
        if (k == null) return;

        (TxtSource.Text, TxtDetail.Text) = k.Source switch
        {
            KeySource.Override => ("Sumber: diatur dari aplikasi (override)",
                                   $"Key berlaku: {k.MaskedKey}" + (k.UpdatedAt is { } t ? $"  ·  diubah {k.UpdatedBy ?? "admin"}, {t:dd MMM yyyy HH:mm}" : string.Empty)),
            KeySource.Environment => ("Sumber: file .env di server",
                                      $"Key berlaku: {k.MaskedKey}. Isi key baru untuk menggantikannya; file .env tidak diubah."),
            _ => ("Belum ada key",
                  "Isi key baru di bawah, atau pasang di file .env server (Integrations__...). Tanpa key, fitur ini memakai data simulasi atau dilewati.")
        };

        TxtWarn.Text = k.Warning ?? string.Empty;
        TxtWarn.Visibility = string.IsNullOrEmpty(k.Warning) ? Visibility.Collapsed : Visibility.Visible;
        BtnReset.Visibility = k.HasOverride ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetResult(string text, Brush color)
    {
        TxtResult.Text = text;
        TxtResult.Foreground = color;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        PwdNew.IsEnabled = !busy;
        BtnTest.IsEnabled = !busy;
        BtnReset.IsEnabled = !busy;
        BtnSave.IsEnabled = !busy && PwdNew.Password.Length > 0;
        if (message != null) SetResult(message, Neutral);
    }

    private void PwdNew_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_busy) BtnSave.IsEnabled = PwdNew.Password.Length > 0;   // kolom kosong = tidak ada yang disimpan
    }

    // ------------------------------------------------------------------ aksi

    private async void Test_Click(object sender, RoutedEventArgs e) => await TestAsync();

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private async void Reset_Click(object sender, RoutedEventArgs e) => await ResetAsync(confirm: true);

    /// <summary>Uji key yang diketik; bila kolom kosong, uji key yang sedang berlaku. Tidak menyimpan apa pun.</summary>
    public async Task TestAsync()
    {
        if (_api == null) return;
        var typed = PwdNew.Password.Trim();
        SetBusy(true, "Menguji koneksi…");
        try
        {
            var r = await _api.TestIntegrationKeyAsync(Provider, string.IsNullOrEmpty(typed) ? null : typed);
            var what = string.IsNullOrEmpty(typed) ? "key yang sedang berlaku" : "key yang diketik";
            SetResult(r.State == ConnectionState.Connected ? $"✔ {r.Message} ({what})" : $"✘ {r.Message} ({what})",
                      r.State == ConnectionState.Connected ? Ok : Bad);
        }
        catch (BackendException ex)
        {
            SetResult("✘ " + ex.Message, Bad);
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Simpan key yang diketik sebagai override. Server mengujinya dulu; key yang gagal tidak menimpa key yang berjalan.</summary>
    public async Task SaveAsync()
    {
        if (_api == null) return;
        var typed = PwdNew.Password.Trim();
        if (typed.Length == 0)
        {
            SetResult("Kolom key baru kosong, jadi tidak ada yang diubah.", Neutral);
            return;
        }

        SetBusy(true, "Menguji lalu menyimpan key…");
        try
        {
            _current = await _api.SetIntegrationKeyAsync(Provider, typed);
            PwdNew.Clear();
            ShowCurrent();
            SetResult("✔ Key lolos uji, disimpan, dan langsung dipakai (tanpa restart server).", Ok);
        }
        catch (BackendException ex)
        {
            SetResult("✘ " + ex.Message, Bad);   // pesan server sudah menjelaskan; key yang berjalan tidak berubah
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Buang override sehingga key dari file .env server berlaku lagi.</summary>
    public async Task ResetAsync(bool confirm)
    {
        if (_api == null) return;
        if (confirm && MessageBox.Show(Window.GetWindow(this),
                "Buang key yang diatur dari aplikasi dan kembali memakai key dari file .env server?\n\nKey yang dibuang tidak bisa dilihat lagi; Anda harus mengetik ulang bila ingin memakainya.",
                "Kembalikan ke default", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        SetBusy(true, "Mengembalikan ke default…");
        try
        {
            _current = await _api.ResetIntegrationKeyAsync(Provider);
            ShowCurrent();
            SetResult(_current.Source == KeySource.Environment
                ? "✔ Kembali memakai key dari file .env server."
                : "✔ Override dibuang. Belum ada key di file .env, jadi fitur ini kembali ke data simulasi/dilewati.", Ok);
        }
        catch (BackendException ex)
        {
            SetResult("✘ " + ex.Message, Bad);
        }
        finally
        {
            SetBusy(false);
        }
    }
}
