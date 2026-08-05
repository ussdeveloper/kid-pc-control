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
    public const string DiscoveryMagic = "KIDPCCTRL1";
    public static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    public static string ProgramDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductFolder);

    public static string PolicyPath => Path.Combine(ProgramDataDir, "policy.json");
    public static string UpdateCachePath => Path.Combine(ProgramDataDir, "update-cache.json");
}
