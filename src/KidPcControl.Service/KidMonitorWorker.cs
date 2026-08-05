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
    private readonly SessionTracker _session;
    private DiscoveryPublisher? _publisher;

    public KidMonitorWorker(ILogger<KidMonitorWorker> logger, PolicyRuntime runtime, SessionTracker session)
    {
        _logger = logger;
        _runtime = runtime;
        _session = session;
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
            DeviceBlocked = false,
            IpAddress = GetLocalIp()
        };

        _publisher = new DiscoveryPublisher(presence);
        _publisher.Start();
        SystemProxy.ApplyFromPolicy(policy);
        _logger.LogInformation("Kid PC Control service started for {Name} ({Id})", policy.DeviceName, policy.DeviceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _runtime.Reload();
                policy = _runtime.Snapshot();
                var status = _session.BuildStatus();

                presence.DeviceName = policy.DeviceName;
                presence.DeviceBlocked = status.Locked;
                presence.IpAddress = GetLocalIp();

                StatusStore.Save(status);
                AppEnforcer.Enforce(policy, status.Locked);

                if (status.Locked)
                    _logger.LogInformation("Enforcing lock: {Reason}", status.LockReason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Monitor loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_publisher is not null)
            await _publisher.DisposeAsync();
        await base.StopAsync(cancellationToken);
    }

    private static string GetLocalIp()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return addr.Address.ToString();
                }
            }
        }
        catch { /* ignore */ }
        return "127.0.0.1";
    }
}
