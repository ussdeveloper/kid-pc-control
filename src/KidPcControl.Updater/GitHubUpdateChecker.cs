using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using KidPcControl.Shared;

namespace KidPcControl.Updater;

public sealed class UpdateCheckResult
{
    public bool UpdateAvailable { get; init; }
    public string? LatestVersion { get; init; }
    public string? DownloadUrl { get; init; }
    public string? Message { get; init; }
    public bool RateLimited { get; init; }
}

public sealed class GitHubUpdateChecker
{
    private readonly HttpClient _http;
    private readonly string _currentVersion;
    private readonly string _cachePath;

    public GitHubUpdateChecker(string currentVersion, HttpClient? http = null, string? cachePath = null)
    {
        _currentVersion = NormalizeVersion(currentVersion);
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("KidPcControl-Updater/0.1");
        if (_http.DefaultRequestHeaders.Accept.Count == 0)
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _cachePath = cachePath ?? AppConstants.UpdateCachePath;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var cache = LoadCache();

        var url = $"https://api.github.com/repos/{AppConstants.GitHubOwner}/{AppConstants.GitHubRepo}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(cache.ETag))
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cache.ETag));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult { Message = $"Update check failed: {ex.Message}" };
        }

        if ((int)response.StatusCode == 304)
            return BuildFromCache(cache, "Not modified (ETag)");

        if ((int)response.StatusCode is 429 or 403)
        {
            return new UpdateCheckResult
            {
                RateLimited = true,
                Message = "GitHub rate limit — next check delayed."
            };
        }

        if (!response.IsSuccessStatusCode)
            return new UpdateCheckResult { Message = $"GitHub HTTP {(int)response.StatusCode}" };

        var json = await response.Content.ReadAsStringAsync(ct);
        var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            return new UpdateCheckResult { Message = "No release payload" };

        cache = new UpdateCache
        {
            ETag = response.Headers.ETag?.Tag,
            LatestVersion = NormalizeVersion(release.TagName),
            DownloadUrl = PickSetupAsset(release),
            CheckedAt = DateTimeOffset.UtcNow
        };
        SaveCache(cache);
        return BuildFromCache(cache, "Checked latest release");
    }

    private UpdateCheckResult BuildFromCache(UpdateCache cache, string message)
    {
        var latest = cache.LatestVersion ?? _currentVersion;
        var newer = CompareSemVer(latest, _currentVersion) > 0;
        return new UpdateCheckResult
        {
            UpdateAvailable = newer,
            LatestVersion = latest,
            DownloadUrl = cache.DownloadUrl,
            Message = message
        };
    }

    private static string? PickSetupAsset(GitHubRelease release) =>
        release.Assets?
            .Select(a => a.BrowserDownloadUrl)
            .FirstOrDefault(u => u is not null && u.Contains("Setup", StringComparison.OrdinalIgnoreCase));

    private UpdateCache LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath))
                return new UpdateCache();
            return JsonSerializer.Deserialize<UpdateCache>(File.ReadAllText(_cachePath)) ?? new UpdateCache();
        }
        catch
        {
            return new UpdateCache();
        }
    }

    private void SaveCache(UpdateCache cache) =>
        File.WriteAllText(_cachePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));

    public static string NormalizeVersion(string version)
    {
        version = version.Trim();
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            version = version[1..];
        return version;
    }

    public static int CompareSemVer(string a, string b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        for (var i = 0; i < 3; i++)
        {
            var c = pa[i].CompareTo(pb[i]);
            if (c != 0) return c;
        }
        return 0;
    }

    private static int[] Parse(string v)
    {
        var parts = NormalizeVersion(v).Split('.', StringSplitOptions.RemoveEmptyEntries);
        var result = new int[3];
        for (var i = 0; i < Math.Min(3, parts.Length); i++)
            int.TryParse(new string(parts[i].TakeWhile(char.IsDigit).ToArray()), out result[i]);
        return result;
    }

    private sealed class UpdateCache
    {
        public string? ETag { get; set; }
        public string? LatestVersion { get; set; }
        public string? DownloadUrl { get; set; }
        public DateTimeOffset CheckedAt { get; set; }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
