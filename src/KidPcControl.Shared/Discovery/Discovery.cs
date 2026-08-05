using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using KidPcControl.Shared.Models;

namespace KidPcControl.Shared.Discovery;

public sealed class DiscoveryBeacon
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static byte[] Encode(KidPresence presence)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(presence, JsonOptions);
        var magic = Encoding.ASCII.GetBytes(AppConstants.DiscoveryMagic);
        var packet = new byte[magic.Length + payload.Length];
        Buffer.BlockCopy(magic, 0, packet, 0, magic.Length);
        Buffer.BlockCopy(payload, 0, packet, magic.Length, payload.Length);
        return packet;
    }

    public static KidPresence? TryDecode(byte[] buffer, int length)
    {
        var magic = Encoding.ASCII.GetBytes(AppConstants.DiscoveryMagic);
        if (length < magic.Length)
            return null;

        for (var i = 0; i < magic.Length; i++)
        {
            if (buffer[i] != magic[i])
                return null;
        }

        try
        {
            return JsonSerializer.Deserialize<KidPresence>(buffer.AsSpan(magic.Length, length - magic.Length), JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class DiscoveryPublisher : IAsyncDisposable
{
    private readonly UdpClient _udp = new() { EnableBroadcast = true };
    private readonly KidPresence _presence;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public DiscoveryPublisher(KidPresence presence)
    {
        _presence = presence;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var endpoint = new IPEndPoint(IPAddress.Broadcast, AppConstants.DiscoveryPort);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _presence.LastSeen = DateTimeOffset.UtcNow;
                var packet = DiscoveryBeacon.Encode(_presence);
                await _udp.SendAsync(packet, packet.Length, endpoint);
            }
            catch
            {
                // ignore transient network errors
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_loop is not null)
            {
                try { await _loop; } catch { /* ignore */ }
            }
            _cts.Dispose();
        }
        _udp.Dispose();
    }
}

public sealed class DiscoveryListener : IAsyncDisposable
{
    private readonly UdpClient _udp;
    private readonly Dictionary<string, KidPresence> _devices = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action? DevicesChanged;

    public DiscoveryListener()
    {
        _udp = new UdpClient(AppConstants.DiscoveryPort);
    }

    public IReadOnlyList<KidPresence> Snapshot()
    {
        lock (_gate)
        {
            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(12);
            return _devices.Values
                .Where(d => d.LastSeen >= cutoff)
                .OrderBy(d => d.DeviceName)
                .ToList();
        }
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(ct);
                var presence = DiscoveryBeacon.TryDecode(result.Buffer, result.Buffer.Length);
                if (presence is null)
                    continue;

                presence.IpAddress = result.RemoteEndPoint.Address.ToString();
                presence.LastSeen = DateTimeOffset.UtcNow;
                presence.Online = true;

                lock (_gate)
                {
                    _devices[presence.DeviceId] = presence;
                }

                DevicesChanged?.Invoke();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // ignore
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_loop is not null)
            {
                try { await _loop; } catch { /* ignore */ }
            }
            _cts.Dispose();
        }
        _udp.Dispose();
    }
}
