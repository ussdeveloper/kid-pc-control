using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KidPcControl.Shared;
using KidPcControl.Shared.Control;
using KidPcControl.Shared.Discovery;
using KidPcControl.Shared.Models;
using KidPcControl.Updater;
using MessageBox = System.Windows.MessageBox;

namespace KidPcControl.Admin;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<KidPresence> _kids = new();
    private readonly ObservableCollection<HourSlotVm> _hours = new();
    private readonly ObservableCollection<AppRuleVm> _appRules = new();
    private readonly ObservableCollection<string> _processHints = new();
    private readonly DiscoveryListener _listener = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _screenTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private KidPresence? _selected;
    private KidApiClient? _client;
    private string? _loadedDeviceId;
    private bool _suppressSelectionLoad;
    private BitmapSource? _lastScreen;

    private readonly CancellationTokenSource _updateCts = new();

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        KidsList.ItemsSource = _kids;
        HoursList.ItemsSource = _hours;
        AppRulesList.ItemsSource = _appRules;
        ProcessHintsList.ItemsSource = _processHints;

        _hours.Add(new HourSlotVm());
        _listener.DevicesChanged += () => Dispatcher.Invoke(RefreshList);
        _listener.Start();
        if (!_listener.IsListening)
            HeaderStatus.Text = "Discovery: port zajęty — zamknij drugą kopię Admina (tray).";
        _refreshTimer.Tick += (_, _) => RefreshList();
        _screenTimer.Tick += async (_, _) => await RefreshScreenAsync();
        _refreshTimer.Start();
        _screenTimer.Start();
        Loaded += (_, _) =>
        {
            UpdateStatusText.Text = $"v{AppUpdateCoordinator.CurrentVersion} · sprawdzam…";
            AppUpdateCoordinator.StartBackgroundLoop(
                initialDelay: TimeSpan.FromSeconds(3),
                interval: AppConstants.UpdateCheckInterval,
                onStatus: msg => Dispatcher.Invoke(() => UpdateStatusText.Text = msg),
                ct: _updateCts.Token);
        };
        Closed += async (_, _) =>
        {
            _updateCts.Cancel();
            _refreshTimer.Stop();
            _screenTimer.Stop();
            await _listener.DisposeAsync();
        };
    }

    public void ForceRefresh() => RefreshList();

    public Task CheckUpdatesNowAsync() => CheckUpdatesAsync();

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void RefreshList()
    {
        var snapshot = _listener.Snapshot();
        var selectedId = (_selected ?? KidsList.SelectedItem as KidPresence)?.DeviceId;

        var incoming = snapshot.ToDictionary(k => k.DeviceId, StringComparer.OrdinalIgnoreCase);
        for (var i = _kids.Count - 1; i >= 0; i--)
        {
            if (!incoming.ContainsKey(_kids[i].DeviceId))
                _kids.RemoveAt(i);
        }

        foreach (var kid in snapshot)
        {
            var existing = _kids.FirstOrDefault(k =>
                string.Equals(k.DeviceId, kid.DeviceId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _kids.Add(kid);
                continue;
            }

            existing.DeviceName = kid.DeviceName;
            existing.HostName = kid.HostName;
            existing.IpAddress = kid.IpAddress;
            existing.ControlPort = kid.ControlPort;
            existing.Version = kid.Version;
            existing.Online = kid.Online;
            existing.DeviceBlocked = kid.DeviceBlocked;
            existing.LastSeen = kid.LastSeen;
        }

        if (selectedId is null) return;

        if (KidsList.SelectedItem is KidPresence cur &&
            string.Equals(cur.DeviceId, selectedId, StringComparison.OrdinalIgnoreCase))
        {
            _selected = cur;
            if (_client is not null &&
                (!string.Equals(_client.BaseUri.Host, cur.IpAddress, StringComparison.OrdinalIgnoreCase) ||
                 _client.BaseUri.Port != cur.ControlPort))
            {
                _client = new KidApiClient(cur.IpAddress, cur.ControlPort);
            }
            SelectedTitle.Text = cur.DeviceName;
            SelectedMeta.Text = $"{cur.HostName} · {cur.IpAddress}:{cur.ControlPort} · v{cur.Version}";
            return;
        }

        var match = _kids.FirstOrDefault(k =>
            string.Equals(k.DeviceId, selectedId, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;

        _suppressSelectionLoad = true;
        KidsList.SelectedItem = match;
        _suppressSelectionLoad = false;
        _selected = match;
    }

    private async void KidsList_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressSelectionLoad) return;

        var kid = KidsList.SelectedItem as KidPresence;
        if (kid is null)
        {
            _selected = null;
            _client = null;
            _loadedDeviceId = null;
            SelectedTitle.Text = "Wybierz urządzenie";
            SelectedMeta.Text = "Po uruchomieniu Kid w LAN pojawi się na liście.";
            return;
        }

        var deviceChanged = !string.Equals(_loadedDeviceId, kid.DeviceId, StringComparison.OrdinalIgnoreCase);
        _selected = kid;
        SelectedTitle.Text = kid.DeviceName;
        SelectedMeta.Text = $"{kid.HostName} · {kid.IpAddress}:{kid.ControlPort} · v{kid.Version}";
        _client = new KidApiClient(kid.IpAddress, kid.ControlPort);

        if (!deviceChanged) return;

        _loadedDeviceId = kid.DeviceId;
        await LoadRemotePolicyAsync();
        await RefreshScreenAsync();
        await LoadUrlsAsync();
        await LoadProcessHintsAsync();
    }

    private async Task LoadRemotePolicyAsync()
    {
        if (_client is null) return;
        var policy = await _client.GetPolicyAsync();
        var status = await _client.GetStatusAsync();
        if (policy is null)
        {
            ActionResult.Text = "Brak połączenia z API Kid (firewall / serwis).";
            return;
        }

        _hours.Clear();
        foreach (var w in policy.AllowedHours)
            _hours.Add(HourSlotVm.From(w));
        if (_hours.Count == 0)
            _hours.Add(new HourSlotVm());

        _appRules.Clear();
        foreach (var r in policy.AppSchedules)
            _appRules.Add(AppRuleVm.From(r));

        MaxUseBox.Text = policy.MaxContinuousMinutes.ToString();
        AllowedAppsBox.Text = string.Join(Environment.NewLine, policy.AllowedApps);
        UrlRegexBox.Text = string.Join(Environment.NewLine, policy.BlockedUrlRegex);
        UrlMessageBox.Text = policy.UrlBlockMessage;
        LockMessageBox.Text = policy.LockMessage;
        HeaderStatus.Text = status is null
            ? "Połączono — brak statusu"
            : $"{status.DeviceName}: {status.LockReason} · {status.UsedMinutes}/{status.MaxContinuousMinutes} min";
        ActionResult.Text = "Polityka pobrana z Kid.";
    }

    private void AddHour_Click(object sender, RoutedEventArgs e) => _hours.Add(new HourSlotVm());

    private void RemoveHour_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is HourSlotVm vm && _hours.Count > 1)
            _hours.Remove(vm);
    }

    private void AddAppRule_Click(object sender, RoutedEventArgs e) =>
        _appRules.Add(new AppRuleVm { ProcessName = "chrome", ModeId = "Block", Start = "08:00", End = "15:00" });

    private void RemoveAppRule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AppRuleVm vm)
            _appRules.Remove(vm);
    }

    private void AddBlockedFromHint_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessHintsList.SelectedItem is not string name || string.IsNullOrWhiteSpace(name))
        {
            ActionResult.Text = "Wybierz proces z listy po prawej w zakładce Aplikacje.";
            return;
        }

        if (_appRules.Any(r => string.Equals(r.ProcessName, name, StringComparison.OrdinalIgnoreCase) && r.ModeId == "Block"))
        {
            ActionResult.Text = $"Reguła blokady dla „{name}” już jest.";
            return;
        }

        _appRules.Add(new AppRuleVm
        {
            ProcessName = name,
            ModeId = "Block",
            Start = "00:00",
            End = "23:59"
        });
        ActionResult.Text = $"Dodano blokadę: {name} (cały dzień). Kliknij „Wyślij politykę”.";
    }

    private async void SavePolicy_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null || _selected is null)
        {
            MessageBox.Show(this, "Wybierz urządzenie Kid.", AppConstants.AppName);
            return;
        }

        try
        {
            var existing = await _client.GetPolicyAsync() ?? new KidPolicy();
            existing.DeviceId = _selected.DeviceId;
            existing.DeviceName = _selected.DeviceName;
            existing.AllowedHours = _hours.Select(h => h.ToModel()).Where(h => !string.IsNullOrWhiteSpace(h.Start)).ToList();
            if (existing.AllowedHours.Count == 0)
                existing.AllowedHours.Add(new AllowedHoursWindow());
            if (int.TryParse(MaxUseBox.Text.Trim(), out var max))
                existing.MaxContinuousMinutes = Math.Clamp(max, 1, 24 * 60);
            existing.AllowedApps = AllowedAppsBox.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            existing.AppSchedules = _appRules
                .Where(r => !string.IsNullOrWhiteSpace(r.ProcessName))
                .Select(r => r.ToModel())
                .ToList();
            existing.BlockedUrlRegex = UrlRegexBox.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            existing.UrlBlockMessage = UrlMessageBox.Text.Trim();
            existing.LockMessage = LockMessageBox.Text.Trim();

            await _client.PushPolicyAsync(existing);
            ActionResult.Text = "Polityka wysłana do Kid — edycja zachowana (bez auto-resetu).";
            HeaderStatus.Text = $"{_selected.DeviceName}: polityka zapisana {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            ActionResult.Text = $"Błąd wysyłki: {ex.Message}";
        }
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null)
        {
            MessageBox.Show(this, "Wybierz urządzenie Kid.", AppConstants.AppName);
            return;
        }

        var pwd = NewAdminPasswordBox.Password ?? string.Empty;
        var confirm = NewAdminPasswordConfirmBox.Password ?? string.Empty;
        if (pwd.Length < 4)
        {
            ActionResult.Text = "Nowe hasło: minimum 4 znaki.";
            return;
        }
        if (pwd != confirm)
        {
            ActionResult.Text = "Hasła nie są zgodne.";
            return;
        }

        try
        {
            await _client.ChangeAdminPasswordAsync(pwd);
            NewAdminPasswordBox.Password = string.Empty;
            NewAdminPasswordConfirmBox.Password = string.Empty;
            ActionResult.Text = "Hasło admina na Kidzie zmienione.";
        }
        catch (Exception ex)
        {
            ActionResult.Text = $"Nie zmieniono hasła: {ex.Message}";
        }
    }

    private async void BlockNow_Click(object sender, RoutedEventArgs e) => await SetBlockedAsync(true);
    private async void Unblock_Click(object sender, RoutedEventArgs e) => await SetBlockedAsync(false);

    private async Task SetBlockedAsync(bool blocked)
    {
        if (_client is null)
        {
            MessageBox.Show(this, "Wybierz urządzenie Kid.", AppConstants.AppName);
            return;
        }
        try
        {
            await _client.SetBlockedAsync(blocked);
            ActionResult.Text = blocked ? "Kid zablokowany." : "Kid odblokowany.";
            var status = await _client.GetStatusAsync();
            if (status is not null)
                HeaderStatus.Text = $"{status.DeviceName}: {status.LockReason}";
        }
        catch (Exception ex)
        {
            ActionResult.Text = ex.Message;
        }
    }

    private async void LoadApps_Click(object sender, RoutedEventArgs e)
    {
        await LoadProcessHintsAsync();
        if (_processHints.Count == 0)
        {
            ActionResult.Text = "Nie pobrano listy aplikacji.";
            return;
        }
        ActionResult.Text = $"Pobrano {_processHints.Count} procesów — kliknij „+ Blokuj wybrany” albo dopisz do whitelisty.";
    }

    private async Task LoadProcessHintsAsync()
    {
        if (_client is null) return;
        var apps = await _client.GetAppsAsync();
        if (apps is null) return;
        _processHints.Clear();
        foreach (var name in apps.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).Take(120))
            _processHints.Add(name);
    }

    private async void RefreshScreen_Click(object sender, RoutedEventArgs e) => await RefreshScreenAsync();

    private void ScreenPreview_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_client is null)
        {
            MessageBox.Show(this, "Wybierz urządzenie Kid.", AppConstants.AppName);
            return;
        }

        var win = new ScreenPreviewWindow(_client, _lastScreen) { Owner = this };
        win.Show();
    }

    private async Task RefreshScreenAsync()
    {
        if (_client is null) return;
        var bytes = await _client.GetScreenJpegAsync();
        if (bytes is null || bytes.Length == 0) return;
        try
        {
            using var ms = new MemoryStream(bytes);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            _lastScreen = img;
            ScreenPreview.Source = img;
        }
        catch { /* ignore */ }
    }

    private async Task LoadUrlsAsync()
    {
        if (_client is null) return;
        var urls = await _client.GetUrlsAsync();
        UrlList.ItemsSource = urls?.Select(u => $"{(u.Blocked ? "[BLOCK] " : "")}{u.Url}").Take(40).ToList()
                              ?? new List<string>();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync();

    private async Task CheckUpdatesAsync()
    {
        try
        {
            UpdateStatusText.Text = $"v{AppUpdateCoordinator.CurrentVersion} · sprawdzam…";
            var (check, apply) = await AppUpdateCoordinator.CheckAndApplyAsync();
            if (apply is { Started: true })
            {
                UpdateStatusText.Text = $"Instaluję v{check.LatestVersion}…";
                return;
            }

            if (check.UpdateAvailable)
            {
                UpdateStatusText.Text = apply?.Message ?? $"Dostępna v{check.LatestVersion}";
                return;
            }

            UpdateStatusText.Text = check.RateLimited
                ? "Aktualizacje: limit GitHub"
                : $"Aktualne (v{AppUpdateCoordinator.CurrentVersion})";
        }
        catch
        {
            UpdateStatusText.Text = "Aktualizacje: offline";
        }
    }
}

public sealed class DayOption
{
    public string Label { get; init; } = "";
    public int Key { get; init; } // -1 = everyday, 0-6 = DayOfWeek
}

public sealed class ModeOption
{
    public string Id { get; init; } = "Block";
    public string Label { get; init; } = "";
}

public abstract class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class HourSlotVm : Bindable
{
    public static IReadOnlyList<DayOption> SharedDays { get; } =
    [
        new() { Label = "Codziennie", Key = -1 },
        new() { Label = "Poniedziałek", Key = (int)DayOfWeek.Monday },
        new() { Label = "Wtorek", Key = (int)DayOfWeek.Tuesday },
        new() { Label = "Środa", Key = (int)DayOfWeek.Wednesday },
        new() { Label = "Czwartek", Key = (int)DayOfWeek.Thursday },
        new() { Label = "Piątek", Key = (int)DayOfWeek.Friday },
        new() { Label = "Sobota", Key = (int)DayOfWeek.Saturday },
        new() { Label = "Niedziela", Key = (int)DayOfWeek.Sunday },
    ];

    public IReadOnlyList<DayOption> DayOptions => SharedDays;

    private int _dayKey = -1;
    private string _start = "07:00";
    private string _end = "21:00";

    public int DayKey { get => _dayKey; set => Set(ref _dayKey, value); }
    public string Start { get => _start; set => Set(ref _start, value); }
    public string End { get => _end; set => Set(ref _end, value); }

    public static HourSlotVm From(AllowedHoursWindow w) => new()
    {
        DayKey = w.DayOfWeek is null ? -1 : (int)w.DayOfWeek.Value,
        Start = w.Start,
        End = w.End
    };

    public AllowedHoursWindow ToModel() => new()
    {
        DayOfWeek = DayKey < 0 ? null : (DayOfWeek)DayKey,
        Start = Start.Trim(),
        End = End.Trim()
    };
}

public sealed class AppRuleVm : Bindable
{
    public static IReadOnlyList<ModeOption> SharedModes { get; } =
    [
        new() { Id = "Block", Label = "Blokuj w godzinach" },
        new() { Id = "AllowOnly", Label = "Tylko w godzinach" },
    ];

    public IReadOnlyList<DayOption> DayOptions => HourSlotVm.SharedDays;
    public IReadOnlyList<ModeOption> ModeOptions => SharedModes;

    private string _processName = "";
    private string _modeId = "Block";
    private int _dayKey = -1;
    private string _start = "08:00";
    private string _end = "15:00";

    public string ProcessName { get => _processName; set => Set(ref _processName, value); }
    public string ModeId { get => _modeId; set => Set(ref _modeId, value); }
    public int DayKey { get => _dayKey; set => Set(ref _dayKey, value); }
    public string Start { get => _start; set => Set(ref _start, value); }
    public string End { get => _end; set => Set(ref _end, value); }

    public static AppRuleVm From(AppTimeRule r) => new()
    {
        ProcessName = r.ProcessName,
        ModeId = string.Equals(r.Mode, "AllowOnly", StringComparison.OrdinalIgnoreCase) ? "AllowOnly" : "Block",
        DayKey = r.DayOfWeek is null ? -1 : (int)r.DayOfWeek.Value,
        Start = r.Start,
        End = r.End
    };

    public AppTimeRule ToModel() => new()
    {
        ProcessName = ProcessName.Trim(),
        Mode = ModeId,
        DayOfWeek = DayKey < 0 ? null : (DayOfWeek)DayKey,
        Start = Start.Trim(),
        End = End.Trim()
    };
}
