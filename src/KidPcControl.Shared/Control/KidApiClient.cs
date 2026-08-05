using System.Net.Http.Json;
using System.Text.Json;
using KidPcControl.Shared.Models;

namespace KidPcControl.Shared.Control;

public sealed class KidApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public KidApiClient(string ip, int port = AppConstants.ControlPort, HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        BaseUri = new Uri($"http://{ip}:{port}/");
    }

    public Uri BaseUri { get; }

    public Task<RuntimeStatus?> GetStatusAsync(CancellationToken ct = default) =>
        GetAsync<RuntimeStatus>("api/status", ct);

    public Task<KidPolicy?> GetPolicyAsync(CancellationToken ct = default) =>
        GetAsync<KidPolicy>("api/policy", ct);

    public async Task PushPolicyAsync(KidPolicy policy, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(new Uri(BaseUri, "api/policy"), policy, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SetBlockedAsync(bool blocked, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(new Uri(BaseUri, "api/block"), new BlockRequest { Blocked = blocked }, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public Task<List<AppInfo>?> GetAppsAsync(CancellationToken ct = default) =>
        GetAsync<List<AppInfo>>("api/apps", ct);

    public Task<List<UrlLogEntry>?> GetUrlsAsync(CancellationToken ct = default) =>
        GetAsync<List<UrlLogEntry>>("api/urls", ct);

    public async Task<byte[]?> GetScreenJpegAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetByteArrayAsync(new Uri(BaseUri, "api/screen.jpg"), ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task SendAnnotationAsync(ScreenAnnotation annotation, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(new Uri(BaseUri, "api/annotate"), annotation, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ChangeAdminPasswordAsync(string newPassword, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            new Uri(BaseUri, "api/admin-password"),
            new AdminPasswordChangeRequest { NewPassword = newPassword },
            JsonOptions,
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task UnlockWithPasswordAsync(string password, int minutes = 30, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            new Uri(BaseUri, "api/unlock"),
            new UnlockRequest { Password = password, Minutes = minutes },
            JsonOptions,
            ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<T>(new Uri(BaseUri, path), JsonOptions, ct);
        }
        catch
        {
            return default;
        }
    }
}
