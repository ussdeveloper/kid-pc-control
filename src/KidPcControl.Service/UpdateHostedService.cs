using KidPcControl.Shared;
using KidPcControl.Updater;

namespace KidPcControl.Service;

public sealed class UpdateHostedService : BackgroundService
{
    private readonly ILogger<UpdateHostedService> _logger;

    public UpdateHostedService(ILogger<UpdateHostedService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (check, apply) = await AppUpdateCoordinator.CheckAndApplyAsync(stoppingToken);
                _logger.LogInformation(
                    "Update check: available={Available} latest={Latest} started={Started} ({Message})",
                    check.UpdateAvailable,
                    check.LatestVersion,
                    apply?.Started,
                    apply?.Message ?? check.Message);

                if (apply is { Started: true })
                {
                    await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
                    continue;
                }

                var delay = check.RateLimited ? TimeSpan.FromHours(12) : AppConstants.UpdateCheckInterval;
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
