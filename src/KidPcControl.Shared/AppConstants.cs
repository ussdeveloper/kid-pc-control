namespace KidPcControl.Shared;

public static class AppConstants
{
    public const string AppName = "Kid PC Control";
    public const string ProductFolder = "KidPcControl";
    public const string ServiceName = "KidPcControlService";
    public const string GitHubOwner = "ussdeveloper";
    public const string GitHubRepo = "kid-pc-control";
    public const int DiscoveryPort = 47891;
    public const int ControlPort = 47892;
    public const int UrlProxyPort = 47893;
    public const string DiscoveryMagic = "KIDPCCTRL1";
    public static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(2);

    public static string ProgramDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductFolder);

    public static string PolicyPath => Path.Combine(ProgramDataDir, "policy.json");
    public static string UpdateCachePath => Path.Combine(ProgramDataDir, "update-cache.json");
    public static string ScreenPath => Path.Combine(ProgramDataDir, "screen.jpg");
    public static string BlockBannerPath => Path.Combine(ProgramDataDir, "url-block.txt");
    public static string AnnotationPath => Path.Combine(ProgramDataDir, "annotation.json");
    public static string ActivityPath => Path.Combine(ProgramDataDir, "activity.json");
    public static string UsagePath => Path.Combine(ProgramDataDir, "active-usage.json");
    /// <summary>Idle grace after last mouse/keyboard input still counts as active use.</summary>
    public static readonly TimeSpan ActivityGrace = TimeSpan.FromSeconds(5);
}
