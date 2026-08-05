using System.Text.Json;
using KidPcControl.Shared.Models;

namespace KidPcControl.Shared.Storage;

public static class StatusStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Path => System.IO.Path.Combine(AppConstants.ProgramDataDir, "status.json");

    public static void Save(RuntimeStatus status)
    {
        Directory.CreateDirectory(AppConstants.ProgramDataDir);
        status.UpdatedAt = DateTimeOffset.UtcNow;
        File.WriteAllText(Path, JsonSerializer.Serialize(status, JsonOptions));
    }

    public static RuntimeStatus? Load()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            return JsonSerializer.Deserialize<RuntimeStatus>(File.ReadAllText(Path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

public static class UrlLogStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Path => System.IO.Path.Combine(AppConstants.ProgramDataDir, "urls.json");

    public static void Append(UrlLogEntry entry, int keep = 500)
    {
        lock (Gate)
        {
            var list = Load();
            list.Insert(0, entry);
            if (list.Count > keep)
                list = list.Take(keep).ToList();
            Directory.CreateDirectory(AppConstants.ProgramDataDir);
            File.WriteAllText(Path, JsonSerializer.Serialize(list, JsonOptions));
        }
    }

    public static List<UrlLogEntry> Load()
    {
        try
        {
            if (!File.Exists(Path)) return new();
            return JsonSerializer.Deserialize<List<UrlLogEntry>>(File.ReadAllText(Path), JsonOptions) ?? new();
        }
        catch
        {
            return new();
        }
    }
}
