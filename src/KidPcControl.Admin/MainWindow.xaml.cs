using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using KidPcControl.Shared;
using KidPcControl.Shared.Discovery;
using KidPcControl.Shared.Models;
using KidPcControl.Shared.Storage;
using KidPcControl.Updater;

namespace KidPcControl.Admin;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<KidPresence> _kids = new();
    private readonly DiscoveryListener _listener = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private KidPresence? _selected;

    public MainWindow()
    {
        InitializeComponent();
        KidsList.ItemsSource = _kids;
        _listener.DevicesChanged += () => Dispatcher.Invoke(RefreshList);
        _listener.Start();
        _refreshTimer.Tick += (_, _) => RefreshList();
        _refreshTimer.Start();
        Loaded += async (_, _) => await CheckUpdatesAsync();
        Closed += async (_, _) =>
        {
            _refreshTimer.Stop();
            await _listener.DisposeAsync();
        };
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void RefreshList()
    {
        var snapshot = _listener.Snapshot();
        var selectedId = _selected?.DeviceId;

        _kids.Clear();
        foreach (var kid in snapshot)
            _kids.Add(kid);

        if (selectedId is not null)
        {
            var match = _kids.FirstOrDefault(k => k.DeviceId == selectedId);
            if (match is not null)
                KidsList.SelectedItem = match;
        }
    }

    private void KidsList_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selected = KidsList.SelectedItem as KidPresence;
        if (_selected is null)
        {
            SelectedTitle.Text = "Wybierz urządzenie";
            SelectedMeta.Text = "Po uruchomieniu trybu Kid w tej samej sieci pojawi się tutaj.";
            return;
        }

        SelectedTitle.Text = _selected.DeviceName;
        SelectedMeta.Text = $"{_selected.HostName} · {_selected.IpAddress}:{_selected.ControlPort} · v{_selected.Version}";
    }

    private void SavePolicy_Click(object sender, RoutedEventArgs e)
    {
        var policy = PolicyStore.LoadOrCreate();
        if (_selected is not null)
        {
            policy.DeviceId = _selected.DeviceId;
            policy.DeviceName = _selected.DeviceName;
        }

        policy.AllowedHours =
        [
            new AllowedHoursWindow { Start = StartHourBox.Text.Trim(), End = EndHourBox.Text.Trim() }
        ];

        if (int.TryParse(MaxUseBox.Text.Trim(), out var max))
            policy.MaxContinuousMinutes = Math.Clamp(max, 1, 24 * 60);

        policy.BlockedUrlRegex = UrlRegexBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        policy.UrlBlockMessage = UrlMessageBox.Text.Trim();
        policy.LockMessage = LockMessageBox.Text.Trim();
        PolicyStore.Save(policy);

        MessageBox.Show(this,
            "Polityka zapisana lokalnie. Synchronizacja sieciowa pojawi się w kolejnej wersji.",
            AppConstants.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BlockNow_Click(object sender, RoutedEventArgs e) => SetBlocked(true);
    private void Unblock_Click(object sender, RoutedEventArgs e) => SetBlocked(false);

    private void SetBlocked(bool blocked)
    {
        var policy = PolicyStore.LoadOrCreate();
        policy.DeviceBlocked = blocked;
        PolicyStore.Save(policy);
        MessageBox.Show(this,
            blocked ? "Ustawiono blokadę w lokalnej polityce." : "Usunięto blokadę w lokalnej polityce.",
            AppConstants.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task CheckUpdatesAsync()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";
            var checker = new GitHubUpdateChecker(version.Split('+')[0]);
            var result = await checker.CheckAsync();
            UpdateStatusText.Text = result.UpdateAvailable
                ? $"Dostępna v{result.LatestVersion}"
                : $"Aktualne (v{version.Split('+')[0]})";
        }
        catch
        {
            UpdateStatusText.Text = "Aktualizacje: brak połączenia";
        }
    }
}
