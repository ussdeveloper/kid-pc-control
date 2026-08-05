using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using KidPcControl.Shared;
using KidPcControl.Shared.Security;
using KidPcControl.Shared.Storage;

namespace KidPcControl.Setup;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF0F1216")!);
        Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFEEF2F7")!);
    }

    private void RoleKid_OnChecked(object sender, RoutedEventArgs e)
    {
        if (KidFields is not null)
            KidFields.Visibility = Visibility.Visible;
    }

    private void RoleKid_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (KidFields is not null)
            KidFields.Visibility = Visibility.Collapsed;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var isKid = RoleKid.IsChecked == true;

        if (isKid && !IsRunningAsAdmin())
        {
            var answer = MessageBox.Show(this,
                "Instalacja trybu Kid wymaga Administratora (usługa Windows).\n\nPotwierdź UAC — Setup uruchomi się ponownie.",
                AppConstants.AppName,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK)
            {
                ErrorText.Text = "Anulowano — bez Administratora nie zainstaluję serwisu.";
                return;
            }
            if (!TryRelaunchElevated())
                ErrorText.Text = "Nie udało się uruchomić Setup jako Administrator (UAC anulowane?).";
            else
                Close();
            return;
        }

        Directory.CreateDirectory(AppConstants.ProgramDataDir);
        File.WriteAllText(Path.Combine(AppConstants.ProgramDataDir, "role.txt"), isKid ? "Kid" : "Admin");

        if (isKid)
        {
            var name = DeviceNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ErrorText.Text = "Podaj nazwę urządzenia.";
                return;
            }

            var password = AdminPasswordBox.Password ?? string.Empty;
            var confirm = AdminPasswordConfirmBox.Password ?? string.Empty;
            if (password.Length < 4)
            {
                ErrorText.Text = "Hasło admina: minimum 4 znaki.";
                return;
            }
            if (password != confirm)
            {
                ErrorText.Text = "Hasła nie są zgodne — wpisz to samo hasło w obu polach.";
                return;
            }

            try
            {
                AdminCredentials.SetPassword(password);
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"Nie zapisano hasła: {ex.Message}";
                return;
            }

            if (!AdminCredentials.VerifyPassword(password))
            {
                ErrorText.Text = "Hasło zapisane, ale weryfikacja nie przeszła — spróbuj ponownie.";
                return;
            }

            var policy = PolicyStore.LoadOrCreate();
            policy.Role = "Kid";
            policy.DeviceName = name;
            policy.AdminPasswordHash = AdminCredentials.ReadHash();
            PolicyStore.Save(policy);

            if (!TryRegisterService(out var serviceError))
            {
                ErrorText.Text = serviceError + "\nHasło zostało zapisane — możesz poprawić usługę i nie musisz zmieniać hasła.";
                return;
            }

            var tray = Path.Combine(AppContext.BaseDirectory, "KidPcControl.Tray.exe");
            var agent = Path.Combine(AppContext.BaseDirectory, "KidPcControl.Agent.exe");
            if (File.Exists(tray)) Autostart.Enable("KidPcControlTray", tray);
            if (File.Exists(agent)) Autostart.Enable("KidPcControlAgent", agent);
            TryCreateLogonTask("KidPcControlTray", tray);
            TryCreateLogonTask("KidPcControlAgent", agent);
            TryStartProcess("KidPcControl.Tray.exe");
            TryStartProcess("KidPcControl.Agent.exe");
            Autostart.Disable("KidPcControlAdmin");
        }
        else
        {
            var admin = Path.Combine(AppContext.BaseDirectory, "KidPcControl.Admin.exe");
            if (File.Exists(admin))
                Autostart.Enable("KidPcControlAdmin", admin);
            Autostart.Disable("KidPcControlTray");
            Autostart.Disable("KidPcControlAgent");
            TryStartProcess("KidPcControl.Admin.exe");
        }

        MessageBox.Show(this,
            isKid
                ? "Kid OK.\nHasło admina zapisane.\nUsługa + tray + agent uruchomione.\nOverride w trayu = to hasło."
                : "Admin OK — działa w trayu.",
            AppConstants.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        Close();
    }

    private bool TryRegisterService(out string error)
    {
        error = string.Empty;
        try
        {
            if (!IsRunningAsAdmin())
            {
                error = "Brak uprawnień Administratora — potwierdź UAC.";
                return false;
            }

            var serviceExe = Path.Combine(AppContext.BaseDirectory, "KidPcControl.Service.exe");
            if (!File.Exists(serviceExe))
            {
                error = "Brak KidPcControl.Service.exe w katalogu instalacji.";
                return false;
            }

            RunSc($"stop \"{AppConstants.ServiceName}\"");
            RunSc($"delete \"{AppConstants.ServiceName}\"");
            RunSc($"create \"{AppConstants.ServiceName}\" binPath= \"{serviceExe}\" start= auto DisplayName= \"Kid PC Control Service\"");
            RunSc($"description \"{AppConstants.ServiceName}\" \"Kid PC Control parental service\"");
            var startCode = RunSc($"start \"{AppConstants.ServiceName}\"");
            OpenFirewall();

            if (startCode != 0)
            {
                // 1056 = already running
                if (startCode != 1056)
                {
                    error = $"sc start zakończył się kodem {startCode}. Sprawdź services.msc → KidPcControlService.";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Usługa: {ex.Message}";
            return false;
        }
    }

    private static void OpenFirewall()
    {
        try
        {
            RunNetsh($"http delete urlacl url=http://+:{AppConstants.ControlPort}/");
            RunNetsh($"http add urlacl url=http://+:{AppConstants.ControlPort}/ user=Everyone");
            RunNetsh($"advfirewall firewall delete rule name=\"KidPcControl Control\"");
            RunNetsh($"advfirewall firewall add rule name=\"KidPcControl Control\" dir=in action=allow protocol=TCP localport={AppConstants.ControlPort}");
        }
        catch { /* ignore */ }
    }

    private static void RunNetsh(string args)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        p?.WaitForExit(10000);
    }

    private static int RunSc(string args)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (p is null) return -1;
        p.WaitForExit(20000);
        return p.ExitCode;
    }

    private static void TryCreateLogonTask(string name, string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/Create /TN \"KidPcControl\\{name}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL LIMITED /F",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit(8000);
        }
        catch { /* ignore */ }
    }

    private static void TryStartProcess(string exeName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, exeName);
            if (!File.Exists(path)) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool TryRelaunchElevated()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return false;
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
            Application.Current.Shutdown();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
