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

        var time = TimeOnly.FromDateTime(localNow);
        foreach (var w in windows)
        {
            if (!TimeOnly.TryParseExact(w.Start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                continue;
            if (!TimeOnly.TryParseExact(w.End, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                continue;

            if (start <= end)
            {
                if (time >= start && time <= end)
                    return true;
            }
            else
            {
                // overnight window
                if (time >= start || time <= end)
                    return true;
            }
        }

        return false;
    }

    public static int EffectiveMaxContinuousMinutes(KidPolicy policy)
    {
        var extra = 0;
        if (policy.DailyOverride is { } o && o.Until > DateTimeOffset.Now)
            extra = o.ExtraMinutes;
        return policy.MaxContinuousMinutes + extra;
    }
}
