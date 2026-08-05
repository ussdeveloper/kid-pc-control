using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using KidPcControl.Shared;
using Application = System.Windows.Application;

namespace KidPcControl.Tray;

public partial class App : Application
{
    private NotifyIcon? _tray;
    private StatusWindow? _statusWindow;
    private Icon? _customIcon;
    private System.Windows.Threading.DispatcherTimer? _keepAlive;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        EnsureAutostart();
        EnsureAgentRunning();

        _customIcon = LoadTrayIcon();
        _statusWindow = CreateStatusWindow();
        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "Kid PC Control",
            Icon = _customIcon ?? SystemIcons.Shield,
            ContextMenuStrip = BuildMenu()
        };
        _tray.DoubleClick += (_, _) => ShowStatus();

        _keepAlive = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _keepAlive.Tick += (_, _) =>
        {
            EnsureAutostart();
            EnsureAgentRunning();
        };
        _keepAlive.Start();
    }

    private static void EnsureAutostart()
    {
        try
        {
            var tray = Path.Combine(AppContext.BaseDirectory, "KidPcControl.Tray.exe");
            var agent = Path.Combine(AppContext.BaseDirectory, "KidPcControl.Agent.exe");
            if (File.Exists(tray)) Autostart.Enable("KidPcControlTray", tray);
            if (File.Exists(agent)) Autostart.Enable("KidPcControlAgent", agent);
        }
        catch { /* ignore */ }
    }

    private static void EnsureAgentRunning()
    {
        try
        {
            if (Process.GetProcessesByName("KidPcControl.Agent").Length > 0)
                return;
            var agent = Path.Combine(AppContext.BaseDirectory, "KidPcControl.Agent.exe");
            if (!File.Exists(agent)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = agent,
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch { /* ignore */ }
    }

    private StatusWindow CreateStatusWindow()
    {
        var win = new StatusWindow();
        win.Closed += (_, _) =>
        {
            if (ReferenceEquals(_statusWindow, win))
                _statusWindow = null;
        };
        return win;
    }

    private static Icon? LoadTrayIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico");
            if (!File.Exists(path))
                path = Path.Combine(AppContext.BaseDirectory, "tray.ico");
            return File.Exists(path) ? new Icon(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Status", null, (_, _) => ShowStatus());
        menu.Items.Add("Override / odblokuj (hasło admina)…", null, (_, _) => ShowOverride());
        // No "Zamknij" — Kid mode must stay running
        return menu;
    }

    private void ShowStatus()
    {
        _statusWindow ??= CreateStatusWindow();
        if (!_statusWindow.IsVisible)
            _statusWindow.Show();
        _statusWindow.WindowState = WindowState.Normal;
        _statusWindow.Activate();
        _statusWindow.Reload();
    }

    private void ShowOverride()
    {
        var dlg = new OverrideWindow();
        dlg.ShowDialog();
        _statusWindow?.Reload();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _keepAlive?.Stop();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _customIcon?.Dispose();
        base.OnExit(e);
    }
}
