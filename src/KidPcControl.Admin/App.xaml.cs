using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace KidPcControl.Admin;

public partial class App : Application
{
    private NotifyIcon? _tray;
    private MainWindow? _main;
    private Icon? _icon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _icon = LoadIcon();
        _main = new MainWindow();
        _main.Closing += Main_Closing;

        _tray = new NotifyIcon
        {
            Visible = true,
            Text = "Kid PC Control — Admin",
            Icon = _icon ?? SystemIcons.Application,
            ContextMenuStrip = BuildMenu()
        };
        _tray.DoubleClick += (_, _) => ShowMain();

        ShowMain();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Otwórz panel Admin", null, (_, _) => ShowMain());
        menu.Items.Add("Odśwież urządzenia", null, (_, _) => _main?.ForceRefresh());
        menu.Items.Add("Sprawdź aktualizacje", null, async (_, _) =>
        {
            if (_main is null) return;
            ShowMain();
            await _main.CheckUpdatesNowAsync();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Zakończ Admin", null, (_, _) => ExitApp());
        return menu;
    }

    private void ShowMain()
    {
        if (_main is null) return;
        _main.Show();
        _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private void Main_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        _main?.Hide();
        _tray?.ShowBalloonTip(
            2500,
            "Kid PC Control",
            "Admin działa w zasobniku systemowym (tray).",
            ToolTipIcon.Info);
    }

    private void ExitApp()
    {
        if (_main is not null)
        {
            _main.Closing -= Main_Closing;
            _main.Close();
        }
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _icon?.Dispose();
        Shutdown();
    }

    private static Icon? LoadIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            return File.Exists(path) ? new Icon(path) : null;
        }
        catch
        {
            return null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _icon?.Dispose();
        base.OnExit(e);
    }
}
