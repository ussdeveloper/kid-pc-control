using System.Globalization;
using KidPcControl.Shared.Models;

namespace KidPcControl.Shared.Policy;

public static class AccessEvaluator
{
    public static bool IsWithinAllowedHours(KidPolicy policy, DateTime localNow)
    {
        if (policy.DailyOverride is { LimitsDisabled: true } o && o.Until > DateTimeOffset.Now)
            return true;

        if (policy.DeviceBlocked)
            return false;

        var windows = policy.AllowedHours
            .Where(w => w.DayOfWeek is null || w.DayOfWeek == localNow.DayOfWeek)
            .ToList();

        if (windows.Count == 0)
            return true;

        return windows.Any(w => IsTimeInWindow(localNow, w.Start, w.End));
    }

    public static int EffectiveMaxContinuousMinutes(KidPolicy policy)
    {
        var extra = 0;
        if (policy.DailyOverride is { } o && o.Until > DateTimeOffset.Now)
            extra = o.ExtraMinutes;
        return policy.MaxContinuousMinutes + extra;
    }

    /// <summary>Returns false when the process should be killed right now.</summary>
    public static bool IsAppCurrentlyAllowed(KidPolicy policy, string processName, DateTime localNow)
    {
        if (policy.DailyOverride is { LimitsDisabled: true } o && o.Until > DateTimeOffset.Now)
            return true;

        var name = Normalize(processName);
        var rules = policy.AppSchedules
            .Where(r => Normalize(r.ProcessName) == name)
            .Where(r => r.DayOfWeek is null || r.DayOfWeek == localNow.DayOfWeek)
            .ToList();

        foreach (var rule in rules)
        {
            var inWindow = IsTimeInWindow(localNow, rule.Start, rule.End);
            if (string.Equals(rule.Mode, "AllowOnly", StringComparison.OrdinalIgnoreCase))
            {
                if (!inWindow) return false;
            }
            else // Block
            {
                if (inWindow) return false;
            }
        }

        if (policy.AllowedApps is { Count: > 0 })
        {
            var allow = policy.AllowedApps.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!allow.Contains(name))
                return false;
        }

        return true;
    }

    public static bool IsTimeInWindow(DateTime localNow, string startText, string endText)
    {
        if (!TimeOnly.TryParseExact(startText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            return false;
        if (!TimeOnly.TryParseExact(endText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            return false;

        var time = TimeOnly.FromDateTime(localNow);
        if (start <= end)
            return time >= start && time <= end;
        return time >= start || time <= end;
    }

    public static string Normalize(string value)
    {
        value = value.Trim();
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            value = value[..^4];
        return value;
    }

    public static string DayLabel(DayOfWeek? day) => day switch
    {
        null => "Codziennie",
        DayOfWeek.Monday => "Poniedziałek",
        DayOfWeek.Tuesday => "Wtorek",
        DayOfWeek.Wednesday => "Środa",
        DayOfWeek.Thursday => "Czwartek",
        DayOfWeek.Friday => "Piątek",
        DayOfWeek.Saturday => "Sobota",
        DayOfWeek.Sunday => "Niedziela",
        _ => "Codziennie"
    };
}
