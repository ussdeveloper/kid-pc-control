using System.Net.NetworkInformation;
using System.Reflection;
using KidPcControl.Shared;
using KidPcControl.Shared.Discovery;
using KidPcControl.Shared.Models;
using KidPcControl.Shared.Policy;
using KidPcControl.Shared.Storage;

namespace KidPcControl.Service;

public sealed class PolicyRuntime
{
    private readonly object _gate = new();
    private KidPolicy _policy;

    public PolicyRuntime()
    {
        _policy = PolicyStore.LoadOrCreate();
    }

    public KidPolicy Snapshot()
    {
        lock (_gate) return Clone(_policy);
    }

    public void Reload()
    {
        lock (_gate) _policy = PolicyStore.LoadOrCreate();
    }

    public void Save(KidPolicy policy)
    {
        lock (_gate)
        {
            _policy = policy;
            PolicyStore.Save(_policy);
        }
    }

    private static KidPolicy Clone(KidPolicy p) =>
        System.Text.Json.JsonSerializer.Deserialize<KidPolicy>(
            System.Text.Json.JsonSerializer.Serialize(p))!;
}

public sealed class KidMonitorWorker : BackgroundService
{
    private readonly ILogger<KidMonitorWorker> _logger;
    private readonly PolicyRuntime _runtime;
    private DiscoveryPublisher? _publisher;
    private DateTime _sessionStarted = DateTime.Now;

    public KidMonitorWorker(ILogger<KidMonitorWorker> logger, PolicyRuntime runtime)
    {
        _logger = logger;
        _runtime = runtime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var policy = _runtime.Snapshot();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? "0.1.0";

        var presence = new KidPresence
        {
            DeviceId = policy.DeviceId,
            DeviceName = policy.DeviceName,
            HostName = Environment.MachineName,
            Version = version.Split('+')[0],
            ControlPort = AppConstants.ControlPort,
            DeviceBlocked = policy.DeviceBlocked || !AccessEvaluator.IsWithinAllowedHours(policy, DateTime.Now),
            IpAddress = GetLocalIp()
        };

        _publisher = new DiscoveryPublisher(presence);
        _publisher.Start();
        _logger.LogInformation("Kid PC Control service started for {Name} ({Id})", policy.DeviceName, policy.DeviceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _runtime.Reload();
                policy = _runtime.Snapshot();
                presence.DeviceName = policy.DeviceName;
                presence.DeviceBlocked = policy.DeviceBlocked || !AccessEvaluator.IsWithinAllowedHours(policy, DateTime.Now);
                presence.IpAddress = GetLocalIp();

                var allowed = AccessEvaluator.IsWithinAllowedHours(policy, DateTime.Now);
                var maxMinutes = AccessEvaluator.EffectiveMaxContinuousMinutes(policy);
                var used = (DateTime.Now - _sessionStarted).TotalMinutes;

                if (!allowed || policy.DeviceBlocked)
                {
                    _logger.LogInformation("Device locked by schedule/policy");
                    // Agent lock UI will react to policy file / IPC in later phase
                }
                else if (used >= maxMinutes)
                {
                    _logger.LogInformation("Max continuous use reached ({Minutes} min)", maxMinutes);
                }

                // Signal file for tray/agent
                WriteStatus(policy, allowed, maxMinutes, used);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Monitor loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_publisher is not null)
            await _publisher.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }

    private static void WriteStatus(KidPolicy policy, bool allowed, int maxMinutes, double used)
    {
        var path = Path.Combine(AppConstants.ProgramDataDir, "status.json");
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            policy.DeviceName,
            policy.DeviceId,
            Allowed = allowed,
            DeviceBlocked = policy.DeviceBlocked,
            MaxContinuousMinutes = maxMinutes,
            UsedMinutes = Math.Round(used, 1),
            OverrideActive = policy.DailyOverride is { } o && o.Until > DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.UtcNow
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static string GetLocalIp()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback)
                    continue;

                var props = ni.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return addr.Address.ToString();
                }
            }
        }
        catch
        {
            // ignore
        }

        return "127.0.0.1";
    }
}
