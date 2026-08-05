using System.Windows;
using System.Windows.Controls;
using KidPcControl.Shared.Models;
using KidPcControl.Shared.Security;
using KidPcControl.Shared.Storage;

namespace KidPcControl.Tray;

public partial class OverrideWindow : Window
{
    public OverrideWindow()
    {
        InitializeComponent();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var policy = PolicyStore.LoadOrCreate();
        if (!PasswordHasher.Verify(PasswordBox.Password, policy.AdminPasswordHash))
        {
            ErrorText.Text = "Nieprawidłowe hasło.";
            return;
        }

        if (!int.TryParse(MinutesBox.Text.Trim(), out var minutes) || minutes < 1)
            minutes = 30;

        var endOfDay = DateTime.Today.AddDays(1).AddTicks(-1);
        var untilTimed = DateTimeOffset.Now.AddMinutes(minutes);
        var action = ActionBox.SelectedIndex;

        policy.DailyOverride = action switch
        {
            0 => new DailyOverride
            {
                Until = untilTimed,
                LimitsDisabled = true,
                Note = $"Disabled for {minutes} minutes"
            },
            1 => new DailyOverride
            {
                Until = endOfDay,
                ExtraMinutes = minutes,
                LimitsDisabled = false,
                Note = $"+{minutes} minutes until end of day"
            },
            _ => new DailyOverride
            {
                Until = endOfDay,
                LimitsDisabled = true,
                Note = "Limits changed until end of day"
            }
        };

        // For "change until end of day", also allow adjusting hours quickly
        if (action == 2)
        {
            policy.AllowedHours =
            [
                new AllowedHoursWindow { Start = "00:00", End = "23:59" }
            ];
        }

        PolicyStore.Save(policy);
        DialogResult = true;
    }
}
