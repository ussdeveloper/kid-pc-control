using System.Runtime.InteropServices;
using System.Text.Json;

namespace KidPcControl.Shared;

/// <summary>User-session input idle time (mouse + keyboard) via GetLastInputInfo.</summary>
public static class UserInput
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static TimeSpan GetIdleTime()
    {
        if (!OperatingSystem.IsWindows())
            return TimeSpan.MaxValue;

        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return TimeSpan.MaxValue;

        var idleMs = unchecked(Environment.TickCount - (int)info.dwTime);
        if (idleMs < 0) idleMs = 0;
        return TimeSpan.FromMilliseconds(idleMs);
    }
}

public static class ActivityStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private sealed class ActivityFile
    {
        public DateTimeOffset At { get; set; }
        public long IdleMs { get; set; }
        public bool Active { get; set; }
    }

    /// <summary>Call from Agent (user session) every ~1s.</summary>
    public static void ReportFromUserSession()
    {
        try
        {
            Directory.CreateDirectory(AppConstants.ProgramDataDir);
            var idle = UserInput.GetIdleTime();
            var active = idle <= AppConstants.ActivityGrace;
            var json = JsonSerializer.Serialize(new ActivityFile
            {
                At = DateTimeOffset.UtcNow,
                IdleMs = (long)Math.Min(idle.TotalMilliseconds, long.MaxValue),
                Active = active
            }, JsonOptions);
            File.WriteAllText(AppConstants.ActivityPath, json);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>True when Agent recently reported activity within grace window.</summary>
    public static bool IsUserActive()
    {
        try
        {
            if (!File.Exists(AppConstants.ActivityPath))
                return false;
            var file = JsonSerializer.Deserialize<ActivityFile>(File.ReadAllText(AppConstants.ActivityPath), JsonOptions);
            if (file is null)
                return false;
            if (DateTimeOffset.UtcNow - file.At > TimeSpan.FromSeconds(4))
                return false;
            return file.Active || file.IdleMs <= (long)AppConstants.ActivityGrace.TotalMilliseconds;
        }
        catch
        {
            return false;
        }
    }
}

public static class ActiveUsageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private sealed class UsageFile
    {
        public string Day { get; set; } = "";
        public double ActiveSeconds { get; set; }
    }

    public static double LoadActiveSecondsToday()
    {
        try
        {
            if (!File.Exists(AppConstants.UsagePath))
                return 0;
            var file = JsonSerializer.Deserialize<UsageFile>(File.ReadAllText(AppConstants.UsagePath), JsonOptions);
            if (file is null)
                return 0;
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            return string.Equals(file.Day, today, StringComparison.Ordinal) ? file.ActiveSeconds : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static void SaveActiveSecondsToday(double seconds)
    {
        try
        {
            Directory.CreateDirectory(AppConstants.ProgramDataDir);
            var file = new UsageFile
            {
                Day = DateTime.Now.ToString("yyyy-MM-dd"),
                ActiveSeconds = Math.Max(0, seconds)
            };
            File.WriteAllText(AppConstants.UsagePath, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch
        {
            // ignore
        }
    }
}
