using System.Reflection;
using KidPcControl.Shared;

namespace KidPcControl.Updater;

/// <summary>
/// Shared check → download → silent Setup for Admin and Kid apps.
/// </summary>
public static class AppUpdateCoordinator
{
    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                ?? "0.0.0";
            return v.Split('+')[0];
        }
    }

    public static async Task<(UpdateCheckResult Check, UpdateApplyResult? Apply)> CheckAndApplyAsync(
        CancellationToken ct = default)
    {
        var checker = new GitHubUpdateChecker(CurrentVersion);
        var check = await checker.CheckAsync(ct);
        if (!check.UpdateAvailable || string.IsNullOrWhiteSpace(check.DownloadUrl))
            return (check, null);

        if (UpdateApplier.IsInProgress)
            return (check, new UpdateApplyResult { Message = "Aktualizacja już trwa." });

        var apply = await UpdateApplier.DownloadAndApplyAsync(check, ct: ct);
        return (check, apply);
    }

    /// <summary>
    /// Fire-and-forget background loop: first check after <paramref name="initialDelay"/>, then every interval.
    /// </summary>
    public static void StartBackgroundLoop(
        TimeSpan? initialDelay = null,
        TimeSpan? interval = null,
        Action<string>? onStatus = null,
        CancellationToken ct = default)
    {
        var first = initialDelay ?? TimeSpan.FromSeconds(20);
        var every = interval ?? AppConstants.UpdateCheckInterval;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(first, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var (check, apply) = await CheckAndApplyAsync(ct);
                    if (apply is { Started: true })
                    {
                        onStatus?.Invoke($"Instaluję v{check.LatestVersion}…");
                        await Task.Delay(TimeSpan.FromHours(6), ct);
                        continue;
                    }

                    if (check.UpdateAvailable)
                        onStatus?.Invoke(apply?.Message ?? $"Dostępna v{check.LatestVersion}");
                    else
                        onStatus?.Invoke($"Aktualne (v{CurrentVersion})");

                    var delay = check.RateLimited ? TimeSpan.FromHours(12) : every;
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    onStatus?.Invoke($"Update: {ex.Message}");
                    try { await Task.Delay(TimeSpan.FromHours(1), ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }, ct);
    }
}
