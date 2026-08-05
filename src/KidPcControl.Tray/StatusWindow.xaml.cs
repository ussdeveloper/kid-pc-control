using System.IO;
using System.Text.Json;
using System.Windows;
using KidPcControl.Shared;

namespace KidPcControl.Tray;

public partial class StatusWindow : Window
{
    public StatusWindow()
    {
        InitializeComponent();
        Reload();
    }

    public void Reload()
    {
        var statusPath = Path.Combine(AppConstants.ProgramDataDir, "status.json");
        if (!File.Exists(statusPath))
        {
            DeviceNameText.Text = "Brak statusu serwisu";
            StatusText.Text = "Uruchom usługę KidPcControlService lub zainstaluj tryb Kid.";
            UsageText.Text = string.Empty;
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(statusPath));
            var root = doc.RootElement;
            DeviceNameText.Text = root.GetProperty("DeviceName").GetString() ?? "Kid";
            var allowed = root.GetProperty("Allowed").GetBoolean();
            var blocked = root.GetProperty("DeviceBlocked").GetBoolean();
            var overrideActive = root.GetProperty("OverrideActive").GetBoolean();
            var used = root.GetProperty("UsedMinutes").GetDouble();
            var max = root.GetProperty("MaxContinuousMinutes").GetInt32();

            StatusText.Text = blocked ? "Urządzenie zablokowane"
                : !allowed ? "Poza dozwolonymi godzinami"
                : overrideActive ? "Override aktywny"
                : "Dostęp dozwolony";

            StatusText.Foreground = (blocked || !allowed)
                ? (System.Windows.Media.Brush)FindResource("DangerBrush")
                : (System.Windows.Media.Brush)FindResource("OkBrush");

            UsageText.Text = $"Użycie sesji: {used:0} / {max} min";
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
