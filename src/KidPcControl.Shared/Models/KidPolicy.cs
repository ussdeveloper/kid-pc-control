namespace KidPcControl.Shared.Models;

public sealed class KidPolicy
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceName { get; set; } = "Kid PC";
    public string Role { get; set; } = "Kid";
    public string AdminPasswordHash { get; set; } = string.Empty;
    public List<AllowedHoursWindow> AllowedHours { get; set; } = new()
    {
        new() { DayOfWeek = null, Start = "07:00", End = "21:00" }
    };
    public int MaxContinuousMinutes { get; set; } = 120;
    public List<string> AllowedApps { get; set; } = new();
    public List<string> BlockedUrlRegex { get; set; } = new();
    public string UrlBlockMessage { get; set; } = "Ta strona jest zablokowana przez rodzica.";
    public string LockMessage { get; set; } = "Czas na przerwę. Poproś rodzica o odblokowanie.";
    public bool DeviceBlocked { get; set; }
    public DailyOverride? DailyOverride { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AllowedHoursWindow
{
    /// <summary>Null = every day.</summary>
    public DayOfWeek? DayOfWeek { get; set; }
    public string Start { get; set; } = "07:00";
    public string End { get; set; } = "21:00";
}

public sealed class DailyOverride
{
    public DateTimeOffset Until { get; set; }
    public int ExtraMinutes { get; set; }
    public bool LimitsDisabled { get; set; }
    public string? Note { get; set; }
}

public sealed class KidPresence
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string HostName { get; set; } = Environment.MachineName;
    public string Version { get; set; } = "0.1.0";
    public string IpAddress { get; set; } = string.Empty;
    public int ControlPort { get; set; } = AppConstants.ControlPort;
    public bool Online { get; set; } = true;
    public bool DeviceBlocked { get; set; }
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
}
