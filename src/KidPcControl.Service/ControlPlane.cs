using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KidPcControl.Shared;
using KidPcControl.Shared.Models;
using KidPcControl.Shared.Policy;
using KidPcControl.Shared.Security;
using KidPcControl.Shared.Storage;

namespace KidPcControl.Service;

public sealed class ControlHttpServer : BackgroundService
{
    private readonly ILogger<ControlHttpServer> _logger;
    private readonly PolicyRuntime _runtime;
    private readonly SessionTracker _session;
    private HttpListener? _listener;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public ControlHttpServer(ILogger<ControlHttpServer> logger, PolicyRuntime runtime, SessionTracker session)
    {
        _logger = logger;
        _runtime = runtime;
        _session = session;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{AppConstants.ControlPort}/");
        try
        {
            _listener.Start();
            _logger.LogInformation("Control API listening on port {Port}", AppConstants.ControlPort);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot bind control port {Port}. Falling back to localhost.", AppConstants.ControlPort);
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{AppConstants.ControlPort}/");
            _listener.Start();
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(stoppingToken);
                _ = Task.Run(() => HandleAsync(ctx), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Control accept error");
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "/";
            var method = ctx.Request.HttpMethod.ToUpperInvariant();

            if (path == "/api/status" && method == "GET")
            {
                await WriteJsonAsync(ctx, _session.BuildStatus());
                return;
            }

            if (path == "/api/policy" && method == "GET")
            {
                await WriteJsonAsync(ctx, _runtime.Snapshot());
                return;
            }

            if (path == "/api/policy" && method == "PUT")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var incoming = JsonSerializer.Deserialize<KidPolicy>(body, JsonOptions);
                if (incoming is null)
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                    return;
                }

                var current = _runtime.Snapshot();
                // Preserve local identity + NEVER allow remote wipe of admin password
                incoming.DeviceId = string.IsNullOrWhiteSpace(incoming.DeviceId) ? current.DeviceId : incoming.DeviceId;
                if (string.IsNullOrWhiteSpace(incoming.DeviceName))
                    incoming.DeviceName = current.DeviceName;
                incoming.AdminPasswordHash = AdminCredentials.ReadHash();
                if (string.IsNullOrWhiteSpace(incoming.AdminPasswordHash))
                    incoming.AdminPasswordHash = current.AdminPasswordHash;
                incoming.Role = "Kid";
                _runtime.Save(incoming);
                SystemProxy.ApplyFromPolicy(incoming);
                await WriteJsonAsync(ctx, incoming);
                return;
            }

            if (path == "/api/block" && method == "POST")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<BlockRequest>(body, JsonOptions) ?? new BlockRequest();
                var policy = _runtime.Snapshot();
                policy.DeviceBlocked = req.Blocked;
                if (!req.Blocked)
                    _session.ResetUsage();
                _runtime.Save(policy);
                await WriteJsonAsync(ctx, _session.BuildStatus());
                return;
            }

            if (path == "/api/apps" && method == "GET")
            {
                await WriteJsonAsync(ctx, AppEnforcer.ListInstalledAndRunning());
                return;
            }

            if (path == "/api/urls" && method == "GET")
            {
                await WriteJsonAsync(ctx, UrlLogStore.Load());
                return;
            }

            if (path == "/api/screen.jpg" && method == "GET")
            {
                if (File.Exists(AppConstants.ScreenPath))
                {
                    var bytes = await File.ReadAllBytesAsync(AppConstants.ScreenPath);
                    ctx.Response.ContentType = "image/jpeg";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                    return;
                }

                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            if (path == "/api/annotate" && method == "POST")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var ann = JsonSerializer.Deserialize<ScreenAnnotation>(body, JsonOptions);
                if (ann is null || ann.Width <= 0 || ann.Height <= 0)
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                    return;
                }

                ann.DurationSeconds = Math.Clamp(ann.DurationSeconds <= 0 ? 15 : ann.DurationSeconds, 3, 120);
                ann.X = Math.Clamp(ann.X, 0, 1);
                ann.Y = Math.Clamp(ann.Y, 0, 1);
                ann.Width = Math.Clamp(ann.Width, 0.01, 1);
                ann.Height = Math.Clamp(ann.Height, 0.01, 1);
                if (ann.X + ann.Width > 1) ann.Width = 1 - ann.X;
                if (ann.Y + ann.Height > 1) ann.Height = 1 - ann.Y;
                ann.Text = (ann.Text ?? string.Empty).Trim();
                if (ann.Text.Length > 240) ann.Text = ann.Text[..240];
                ann.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ann.DurationSeconds);

                Directory.CreateDirectory(AppConstants.ProgramDataDir);
                await File.WriteAllTextAsync(
                    AppConstants.AnnotationPath,
                    JsonSerializer.Serialize(ann, JsonOptions));
                await WriteJsonAsync(ctx, ann);
                return;
            }

            if (path == "/api/unlock" && method == "POST")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<UnlockRequest>(body, JsonOptions) ?? new UnlockRequest();
                if (!AdminCredentials.VerifyPassword(req.Password ?? string.Empty))
                {
                    ctx.Response.StatusCode = 401;
                    await WriteJsonAsync(ctx, new { ok = false, error = "bad_password" });
                    return;
                }

                var minutes = Math.Clamp(req.Minutes <= 0 ? 30 : req.Minutes, 5, 24 * 60);
                var policy = _runtime.Snapshot();
                policy.DeviceBlocked = false;
                policy.DailyOverride = new DailyOverride
                {
                    Until = DateTimeOffset.Now.AddMinutes(minutes),
                    LimitsDisabled = true,
                    Note = $"Unlocked with admin password for {minutes} min"
                };
                _runtime.Save(policy);
                _session.ResetUsage();
                await WriteJsonAsync(ctx, _session.BuildStatus());
                return;
            }

            if (path == "/api/admin-password" && method == "POST")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<AdminPasswordChangeRequest>(body, JsonOptions);
                var pwd = req?.NewPassword?.Trim() ?? string.Empty;
                if (pwd.Length < 4)
                {
                    ctx.Response.StatusCode = 400;
                    await WriteJsonAsync(ctx, new { ok = false, error = "password_too_short" });
                    return;
                }

                AdminCredentials.SetPassword(pwd);
                await WriteJsonAsync(ctx, new { ok = true });
                return;
            }

            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Control handler error");
            try
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.Close();
            }
            catch { /* ignore */ }
        }
    }

    private static async Task WriteJsonAsync<T>(HttpListenerContext ctx, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try { _listener?.Stop(); } catch { /* ignore */ }
        _listener?.Close();
        return base.StopAsync(cancellationToken);
    }
}

public sealed class SessionTracker
{
    private readonly PolicyRuntime _runtime;
    private readonly object _gate = new();
    private double _activeSeconds;
    private DateTime _lastTick = DateTime.UtcNow;
    private string _day = DateTime.Now.ToString("yyyy-MM-dd");

    public SessionTracker(PolicyRuntime runtime)
    {
        _runtime = runtime;
        _activeSeconds = ActiveUsageStore.LoadActiveSecondsToday();
    }

    public void ResetUsage()
    {
        lock (_gate)
        {
            _activeSeconds = 0;
            _lastTick = DateTime.UtcNow;
            _day = DateTime.Now.ToString("yyyy-MM-dd");
            ActiveUsageStore.SaveActiveSecondsToday(0);
        }
    }

    /// <summary>Accumulate only while user is actively using mouse/keyboard (+ grace).</summary>
    public void Tick()
    {
        lock (_gate)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (!string.Equals(_day, today, StringComparison.Ordinal))
            {
                _day = today;
                _activeSeconds = 0;
            }

            var now = DateTime.UtcNow;
            var dt = (now - _lastTick).TotalSeconds;
            _lastTick = now;
            if (dt <= 0 || dt > 30)
                return;

            var policy = _runtime.Snapshot();
            var allowed = AccessEvaluator.IsWithinAllowedHours(policy, DateTime.Now);
            var overrideActive = policy.DailyOverride is { } o && o.Until > DateTimeOffset.Now && o.LimitsDisabled;
            if (policy.DeviceBlocked || (!allowed && !overrideActive))
                return;

            // Already over quota → locked → don't keep counting
            var max = AccessEvaluator.EffectiveMaxContinuousMinutes(policy);
            if (_activeSeconds / 60.0 >= max && !overrideActive)
                return;

            if (!ActivityStore.IsUserActive())
                return;

            _activeSeconds += dt;
            ActiveUsageStore.SaveActiveSecondsToday(_activeSeconds);
        }
    }

    public double UsedMinutes
    {
        get { lock (_gate) return _activeSeconds / 60.0; }
    }

    public RuntimeStatus BuildStatus()
    {
        Tick();
        var policy = _runtime.Snapshot();
        var allowed = AccessEvaluator.IsWithinAllowedHours(policy, DateTime.Now);
        var max = AccessEvaluator.EffectiveMaxContinuousMinutes(policy);
        var used = UsedMinutes;
        var overrideActive = policy.DailyOverride is { } o && o.Until > DateTimeOffset.Now;
        var limitsOff = overrideActive && policy.DailyOverride!.LimitsDisabled;
        var lockedByQuota = allowed && !policy.DeviceBlocked && !limitsOff && used >= max;

        var reason = policy.DeviceBlocked ? "Zablokowane przez rodzica"
            : !allowed && !limitsOff ? "Poza dozwolonymi godzinami"
            : lockedByQuota ? "Limit aktywnego czasu wyczerpany"
            : "OK";

        return new RuntimeStatus
        {
            DeviceId = policy.DeviceId,
            DeviceName = policy.DeviceName,
            AllowedBySchedule = allowed || limitsOff,
            DeviceBlocked = policy.DeviceBlocked,
            LockedByQuota = lockedByQuota,
            MaxContinuousMinutes = max,
            UsedMinutes = Math.Round(used, 1),
            OverrideActive = overrideActive,
            LockReason = reason,
            LockMessage = policy.LockMessage,
            RecentUrls = UrlLogStore.Load().Take(20).Select(u => u.Url).ToList(),
            RunningApps = AppEnforcer.ListRunningProcessNames()
        };
    }
}

public static class AppEnforcer
{
    private static readonly HashSet<string> AlwaysAllow = new(StringComparer.OrdinalIgnoreCase)
    {
        "KidPcControl.Service", "KidPcControl.Tray", "KidPcControl.Agent", "KidPcControl.Setup",
        "explorer", "dwm", "csrss", "winlogon", "services", "lsass", "svchost", "System",
        "RuntimeBroker", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost",
        "TextInputHost", "ApplicationFrameHost", "SystemSettings", "SecurityHealthSystray",
        "conhost", "sihost", "taskhostw", "ctfmon", "fontdrvhost", "smss", "wininit"
    };

    public static List<string> ListRunningProcessNames() =>
        Process.GetProcesses()
            .Select(p => { try { return p.ProcessName; } catch { return null; } })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .Cast<string>()
            .ToList();

    public static List<AppInfo> ListInstalledAndRunning()
    {
        var map = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                string path = "";
                try { path = p.MainModule?.FileName ?? ""; } catch { /* access denied */ }
                if (!map.ContainsKey(name))
                    map[name] = new AppInfo { Name = name, Path = path };
            }
            catch { /* ignore */ }
        }
        return map.Values.OrderBy(a => a.Name).ToList();
    }

    public static void Enforce(KidPolicy policy, bool deviceLocked)
    {
        if (deviceLocked)
        {
            // When locked, kill non-essential interactive apps (aggressive but required)
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (AlwaysAllow.Contains(p.ProcessName)) continue;
                    if (string.IsNullOrEmpty(p.MainWindowTitle) && p.SessionId == 0) continue;
                    // Don't kill everything in session 0
                    if (p.SessionId == 0) continue;
                    p.Kill(entireProcessTree: true);
                }
                catch { /* ignore */ }
            }
            return;
        }

        if (policy.AllowedApps is not { Count: > 0 } && policy.AppSchedules is not { Count: > 0 })
            return;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.SessionId == 0) continue;
                if (AlwaysAllow.Contains(p.ProcessName)) continue;
                if (string.IsNullOrEmpty(p.MainWindowTitle)) continue;
                if (AccessEvaluator.IsAppCurrentlyAllowed(policy, p.ProcessName, DateTime.Now))
                    continue;
                p.Kill(entireProcessTree: true);
            }
            catch { /* ignore */ }
        }
    }

    private static string NormalizeApp(string value) => AccessEvaluator.Normalize(value);
}

public static class SystemProxy
{
    public static void ApplyFromPolicy(KidPolicy policy)
    {
        // Enable system proxy to local URL filter when any regex is configured
        var enable = policy.BlockedUrlRegex.Count > 0;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (enable)
            {
                key.SetValue("ProxyEnable", 1);
                key.SetValue("ProxyServer", $"127.0.0.1:{AppConstants.UrlProxyPort}");
            }
            else
            {
                // only clear if our proxy was set
                var server = key.GetValue("ProxyServer") as string ?? "";
                if (server.Contains($"{AppConstants.UrlProxyPort}"))
                {
                    key.SetValue("ProxyEnable", 0);
                }
            }
        }
        catch
        {
            // Service may not have user hive; Agent can also apply
        }
    }
}

public sealed class UrlFilterProxy : BackgroundService
{
    private readonly ILogger<UrlFilterProxy> _logger;
    private readonly PolicyRuntime _runtime;
    private HttpListener? _listener;

    public UrlFilterProxy(ILogger<UrlFilterProxy> logger, PolicyRuntime runtime)
    {
        _logger = logger;
        _runtime = runtime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{AppConstants.UrlProxyPort}/");
        try
        {
            _listener.Start();
            _logger.LogInformation("URL proxy on {Port}", AppConstants.UrlProxyPort);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "URL proxy failed to start");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(stoppingToken);
                _ = Task.Run(() => HandleAsync(ctx), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogDebug(ex, "proxy accept"); }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var url = ctx.Request.RawUrl ?? "/";
        // Absolute URL when used as proxy
        if (ctx.Request.Url is { IsAbsoluteUri: true } u && u.Scheme is "http" or "https")
            url = u.ToString();
        else if (ctx.Request.Headers["Host"] is { Length: > 0 } host)
            url = $"{ctx.Request.Url?.Scheme ?? "http"}://{host}{ctx.Request.RawUrl}";

        var policy = _runtime.Snapshot();
        var blocked = IsBlocked(url, policy.BlockedUrlRegex);
        UrlLogStore.Append(new UrlLogEntry { Url = url, Blocked = blocked, Process = "system-proxy" });

        if (blocked)
        {
            try { File.WriteAllText(AppConstants.BlockBannerPath, policy.UrlBlockMessage); } catch { /* ignore */ }
            var html = $"<html><body style='background:#202020;color:#fff;font-family:Segoe UI;padding:40px'><h1>{WebUtility.HtmlEncode(policy.UrlBlockMessage)}</h1></body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.StatusCode = 403;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
            return;
        }

        // Minimal forward for HTTP (HTTPS CONNECT not fully supported in HttpListener — log Host still)
        if (ctx.Request.HttpMethod == "CONNECT")
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Close();
            return;
        }

        try
        {
            using var client = new HttpClient();
            var target = ctx.Request.Url!;
            using var req = new HttpRequestMessage(new HttpMethod(ctx.Request.HttpMethod), target);
            using var resp = await client.SendAsync(req);
            ctx.Response.StatusCode = (int)resp.StatusCode;
            ctx.Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            var data = await resp.Content.ReadAsByteArrayAsync();
            await ctx.Response.OutputStream.WriteAsync(data);
            ctx.Response.Close();
        }
        catch
        {
            ctx.Response.StatusCode = 502;
            ctx.Response.Close();
        }
    }

    private static bool IsBlocked(string url, List<string> patterns)
    {
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try
            {
                if (Regex.IsMatch(url, p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return true;
            }
            catch
            {
                if (url.Contains(p, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try { _listener?.Stop(); } catch { /* ignore */ }
        return base.StopAsync(cancellationToken);
    }
}
