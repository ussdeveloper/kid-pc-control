using System.Text.Json;
using KidPcControl.Shared.Models;

namespace KidPcControl.Shared.Storage;

public static class PolicyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static KidPolicy LoadOrCreate(string? path = null)
    {
        path ??= AppConstants.PolicyPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            var created = new KidPolicy();
            Save(created, path);
            return created;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<KidPolicy>(json, JsonOptions) ?? new KidPolicy();
    }

    public static void Save(KidPolicy policy, string? path = null)
    {
        path ??= AppConstants.PolicyPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        policy.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(policy, JsonOptions);
        File.WriteAllText(path, json);
    }
}
