using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace KidPcControl.Tray;

public partial class App : Application
{
    private NotifyIcon? _tray;
    private StatusWindow? _statusWindow;
    private Icon? _customIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _customIcon = LoadTrayIcon();
        _statusWindow = new StatusWindow();
        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "Kid PC Control",
            Icon = _customIcon ?? SystemIcons.Shield,
            ContextMenuStrip = BuildMenu()
        };
        _tray.DoubleClick += (_, _) => ShowStatus();
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
        menu.Items.Add("Override (hasło admina)…", null, (_, _) => ShowOverride());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Zamknij tray", null, (_, _) =>
        {
            _tray!.Visible = false;
            Shutdown();
        });
        return menu;
    }

    private void ShowStatus()
    {
        _statusWindow ??= new StatusWindow();
        _statusWindow.Show();
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
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _customIcon?.Dispose();
        base.OnExit(e);
    }
}
