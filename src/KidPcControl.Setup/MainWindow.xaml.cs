using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using KidPcControl.Shared;
using KidPcControl.Shared.Models;
using KidPcControl.Shared.Security;
using KidPcControl.Shared.Storage;

namespace KidPcControl.Setup;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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

        Directory.CreateDirectory(AppConstants.ProgramDataDir);
        var rolePath = Path.Combine(AppConstants.ProgramDataDir, "role.txt");
        File.WriteAllText(rolePath, isKid ? "Kid" : "Admin");

        if (isKid)
        {
            var name = DeviceNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ErrorText.Text = "Podaj nazwę urządzenia.";
                return;
            }

            if (string.IsNullOrWhiteSpace(AdminPasswordBox.Password) ||
                AdminPasswordBox.Password != AdminPasswordConfirmBox.Password)
            {
                ErrorText.Text = "Hasła admina muszą być zgodne i niepuste.";
                return;
            }

            var policy = PolicyStore.LoadOrCreate();
            policy.Role = "Kid";
            policy.DeviceName = name;
            policy.AdminPasswordHash = PasswordHasher.Hash(AdminPasswordBox.Password);
            PolicyStore.Save(policy);

            TryRegisterService();
            TryStartProcess("KidPcControl.Tray.exe");
            TryStartProcess("KidPcControl.Agent.exe");
        }
        else
        {
            File.WriteAllText(Path.Combine(AppConstants.ProgramDataDir, "role.txt"), "Admin");
            TryStartProcess("KidPcControl.Admin.exe");
        }

        MessageBox.Show(this,
            isKid
                ? "Skonfigurowano tryb Kid. Usługa i tray powinny być aktywne."
                : "Skonfigurowano tryb Admin.",
            AppConstants.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        Close();
    }

    private void TryRegisterService()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var serviceExe = Path.Combine(baseDir, "KidPcControl.Service.exe");
            if (!File.Exists(serviceExe))
            {
                // During dev, look in sibling publish folders
                var candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "KidPcControl.Service", "bin", "Debug", "net8.0", "KidPcControl.Service.exe"));
                if (File.Exists(candidate))
                    serviceExe = candidate;
            }

            if (!File.Exists(serviceExe))
            {
                ErrorText.Text = "Nie znaleziono KidPcControl.Service.exe — zapisono politykę; zarejestruj usługę ręcznie.";
                return;
            }

            RunSc($"stop {AppConstants.ServiceName}");
            RunSc($"delete {AppConstants.ServiceName}");
            RunSc($"create {AppConstants.ServiceName} binPath= \"{serviceExe}\" start= auto DisplayName= \"Kid PC Control Service\"");
            RunSc($"description {AppConstants.ServiceName} \"Parental control monitoring for Kid PC Control\"");
            RunSc($"start {AppConstants.ServiceName}");
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Usługa: {ex.Message} (uruchom Setup jako Administrator).";
        }
    }

    private static void RunSc(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(15000);
    }

    private static void TryStartProcess(string exeName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, exeName);
            if (!File.Exists(path))
                return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }
}
