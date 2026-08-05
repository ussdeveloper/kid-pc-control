using System.IO;
using System.Text.Json;
using System.Windows;
using KidPcControl.Shared;
using KidPcControl.Shared.Storage;

namespace KidPcControl.Tray;

public partial class StatusWindow : Window
{
    public StatusWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            // Tray owns this window — X hides it so it can be shown again
            e.Cancel = true;
            Hide();
        };
        Reload();
    }

    public void Reload()
    {
        var status = StatusStore.Load();
        if (status is null)
        {
            var statusPath = Path.Combine(AppConstants.ProgramDataDir, "status.json");
            if (!File.Exists(statusPath))
            {
                DeviceNameText.Text = "Brak statusu serwisu";
                StatusText.Text = "Uruchom usługę KidPcControlService lub zainstaluj tryb Kid.";
                UsageText.Text = string.Empty;
                return;
            }
        }

        if (status is not null)
        {
            DeviceNameText.Text = status.DeviceName;
            StatusText.Text = status.Locked ? status.LockReason : "Dostęp dozwolony";
            StatusText.Foreground = status.Locked
                ? (System.Windows.Media.Brush)FindResource("DangerBrush")
                : (System.Windows.Media.Brush)FindResource("OkBrush");
            UsageText.Text = $"Czas aktywności (mysz/klawiatura): {status.UsedMinutes:0} / {status.MaxContinuousMinutes} min"
                             + (status.OverrideActive ? " · override aktywny" : "");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppConstants.ProgramDataDir, "status.json")));
            var root = doc.RootElement;
            DeviceNameText.Text = root.TryGetProperty("deviceName", out var n) ? n.GetString()
                : root.GetProperty("DeviceName").GetString() ?? "Kid";
            StatusText.Text = "Status odczytany";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Nie można odczytać statusu: {ex.Message}";
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => Reload();

    private void Override_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OverrideWindow { Owner = this };
        dlg.ShowDialog();
        Reload();
    }
}
