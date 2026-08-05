using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using KidPcControl.Shared;
using KidPcControl.Shared.Policy;
using KidPcControl.Shared.Storage;

namespace KidPcControl.Agent;

public partial class LockWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public LockWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Evaluate();
        _timer.Start();
        Loaded += (_, _) => Evaluate();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Evaluate();

    private void Evaluate()
    {
        var policy = PolicyStore.LoadOrCreate();
        MessageText.Text = string.IsNullOrWhiteSpace(policy.LockMessage)
            ? "Czas na przerwę."
            : policy.LockMessage;

        var allowed = AccessEvaluator.IsWithinAllowedHours(policy, DateTime.Now) && !policy.DeviceBlocked;
        if (allowed)
        {
            // Soft mode for now: hide lock when allowed. Full kiosk hardening later.
            Hide();
        }
        else
        {
            Show();
            Activate();
        }

        // Also reflect service status message if present
        var statusPath = Path.Combine(AppConstants.ProgramDataDir, "status.json");
        if (File.Exists(statusPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(statusPath));
                if (doc.RootElement.TryGetProperty("Allowed", out var allowedEl) && allowedEl.GetBoolean()
                    && doc.RootElement.TryGetProperty("DeviceBlocked", out var blockedEl) && !blockedEl.GetBoolean())
                {
                    Hide();
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
