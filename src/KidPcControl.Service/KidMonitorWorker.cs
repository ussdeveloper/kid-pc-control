using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using KidPcControl.Shared;
using KidPcControl.Shared.Discovery;
using KidPcControl.Shared.Models;
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
    private int _watchdogTick;

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
                _session.Tick();
                var status = _session.BuildStatus();

                presence.DeviceName = policy.DeviceName;
                presence.DeviceBlocked = status.Locked;
                presence.IpAddress = GetLocalIp();

                StatusStore.Save(status);
                AppEnforcer.Enforce(policy, status.Locked);

                _watchdogTick++;
                if (_watchdogTick % 5 == 0)
                    EnsureKidAppsRunning();

                if (status.Locked)
                    _logger.LogInformation("Enforcing lock: {Reason}", status.LockReason);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Monitor loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private void EnsureKidAppsRunning()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath)
                      ?? AppContext.BaseDirectory;
            EnsureLogonTask("KidPcControlTray", Path.Combine(dir, "KidPcControl.Tray.exe"));
            EnsureLogonTask("KidPcControlAgent", Path.Combine(dir, "KidPcControl.Agent.exe"));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Watchdog skip");
        }
    }

    private void EnsureLogonTask(string name, string exePath)
    {
        if (!File.Exists(exePath))
            return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/Create /TN \"KidPcControl\\{name}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL LIMITED /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Process.Start(psi)?.WaitForExit(8000);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "schtasks {Name}", name);
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
