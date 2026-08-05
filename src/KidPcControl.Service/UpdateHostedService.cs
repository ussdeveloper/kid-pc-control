using System.Reflection;
using KidPcControl.Shared;
using KidPcControl.Updater;

namespace KidPcControl.Service;

public sealed class UpdateHostedService : BackgroundService
{
    private readonly ILogger<UpdateHostedService> _logger;
    private readonly GitHubUpdateChecker _checker;

    public UpdateHostedService(ILogger<UpdateHostedService> logger)
    {
        _logger = logger;
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";
        _checker = new GitHubUpdateChecker(version.Split('+')[0]);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _checker.CheckAsync(stoppingToken);
                _logger.LogInformation(
                    "Update check: available={Available} latest={Latest} ({Message})",
                    result.UpdateAvailable, result.LatestVersion, result.Message);

                if (result.UpdateAvailable && !string.IsNullOrWhiteSpace(result.DownloadUrl) && !UpdateApplier.IsInProgress)
                {
                    _logger.LogInformation("Applying update to v{Version}…", result.LatestVersion);
                    var apply = await UpdateApplier.DownloadAndApplyAsync(result, ct: stoppingToken);
                    _logger.LogInformation("Update apply: started={Started} {Message}", apply.Started, apply.Message);
                    // After silent setup, process/service may be restarted — wait longer either way
                    await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
                    continue;
                }

                var delay = result.RateLimited ? TimeSpan.FromHours(12) : AppConstants.UpdateCheckInterval;
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Update check failed");
                try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
