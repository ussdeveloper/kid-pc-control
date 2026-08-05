using System.Text.Json.Serialization;

namespace KidPcControl.Shared.Models;

public sealed class RuntimeStatus
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public bool AllowedBySchedule { get; set; } = true;
    public bool DeviceBlocked { get; set; }
    public bool LockedByQuota { get; set; }
    public bool Locked => DeviceBlocked || !AllowedBySchedule || LockedByQuota;
    public int MaxContinuousMinutes { get; set; }
    public double UsedMinutes { get; set; }
    public bool OverrideActive { get; set; }
    public string LockReason { get; set; } = string.Empty;
    public string LockMessage { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<string> RecentUrls { get; set; } = new();
    public List<string> RunningApps { get; set; } = new();
}

public sealed class UrlLogEntry
{
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string Url { get; set; } = string.Empty;
    public string Process { get; set; } = "proxy";
    public bool Blocked { get; set; }
}

public sealed class AppInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public sealed class BlockRequest
{
    public bool Blocked { get; set; }
}

public sealed class UnlockRequest
{
    public string Password { get; set; } = string.Empty;
    /// <summary>Minutes of limits-disabled after unlock (default 30).</summary>
    public int Minutes { get; set; } = 30;
}

public sealed class AdminPasswordChangeRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Normalized 0–1 rectangle on primary screen + text shown briefly on Kid.</summary>
public sealed class ScreenAnnotation
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Text { get; set; } = string.Empty;
    public int DurationSeconds { get; set; } = 15;
    public DateTimeOffset ExpiresAt { get; set; }
}
