using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EdgeDrop;

public class DiscoveryPacket
{
    public string App { get; set; } = "EdgeDrop";
    public int Version { get; set; } = 1;
    public string Type { get; set; } = "beacon";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public int Port { get; set; } = 11000;
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public class DeviceDiscoveryService : IDisposable
{
    private readonly Settings _settings;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private System.Timers.Timer? _beaconTimer;
    private readonly object _lock = new();
    private bool _isRunning;
    private const string MulticastGroupIp = "239.255.110.1";
    public event Action<DeviceItem>? NewDeviceDiscovered;
    public event Action<DeviceItem>? DeviceIpUpdated;
    public event Action<DeviceItem>? DeviceOnlineStatusChanged;

    public DeviceDiscoveryService(Settings settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isRunning) return;

            try
            {
                int port = _settings.DiscoveryPort > 0 ? _settings.DiscoveryPort : 11001;

                _cts = new CancellationTokenSource();

                _udpClient = new UdpClient();

                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));

                _udpClient.EnableBroadcast = true;

                _udpClient.MulticastLoopback = true;

                try
                {
                    _udpClient.JoinMulticastGroup(IPAddress.Parse(MulticastGroupIp));
                }

                catch { }

                _isRunning = true;

                _ = ReceiveLoopAsync(_cts.Token);

                _beaconTimer = new System.Timers.Timer(3500) { AutoReset = true };

                _beaconTimer.Elapsed += (_, _) =>
                {
                    _ = SendPacketAsync("beacon");

                    SweepOfflineDevices();
                };

                _beaconTimer.Start();

                _ = SendPacketAsync("query");

                AppLogger.Info($"Device auto-discovery active on UDP port {port}", "Discovery");
            }

            catch (Exception ex)
            {
                AppLogger.Warning($"Failed to start device auto-discovery: {ex.Message}", "Discovery");

                _isRunning = false;
            }
        }
    }

    public void BroadcastQuery()
    {
        if (!_isRunning) Start();

        _ = SendPacketAsync("query");
    }

    public async Task SendPacketAsync(string type)
    {
        if (_udpClient == null) return;

        try
        {
            var packet = new DiscoveryPacket
            {
                App = "EdgeDrop",
                Version = 1,
                Type = type,
                DeviceId = _settings.DeviceId,
                DeviceName = !string.IsNullOrWhiteSpace(_settings.DeviceName) ? _settings.DeviceName : Environment.MachineName,
                Port = _settings.Port,

                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            string json = JsonSerializer.Serialize(packet);

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            int port = _settings.DiscoveryPort > 0 ? _settings.DiscoveryPort : 11001;

            try
            {
                await _udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, port));
            }

            catch { }

            try
            {
                await _udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse(MulticastGroupIp), port));
            }

            catch { }

            foreach (var subBroadcast in GetSubnetBroadcastAddresses())
            {
                try
                {
                    await _udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(subBroadcast, port));
                }

                catch { }
            }

            if (_settings.LoopbackMode)
            {
                try
                {
                    await _udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, port));
                }

                catch { }
            }

            List<string> knownIps;

            lock (_settings.DevicesLock)
            {
                knownIps = _settings.Devices

                    .Select(d => d.Ip?.Trim())

                    .Where(ip => !string.IsNullOrEmpty(ip) && IPAddress.TryParse(ip, out _))

                    .Distinct()

                    .ToList()!;
            }

            foreach (var ipStr in knownIps)
            {
                try
                {
                    if (IPAddress.TryParse(ipStr, out var directIp))
                    {
                        await _udpClient.SendAsync(bytes, bytes.Length, new IPEndPoint(directIp, port));
                    }
                }

                catch { }
            }
        }

        catch { }
    }

    public void MarkDeviceActive(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;

        if (ip.StartsWith("::ffff:")) ip = ip.Substring(7);

        if (ip == "::1") ip = "127.0.0.1";

        lock (_settings.DevicesLock)
        {
            var matched = _settings.Devices.FirstOrDefault(d =>

                string.Equals(d.Ip?.Trim(), ip, StringComparison.OrdinalIgnoreCase));

            if (matched != null)
            {
                bool wasOffline = !matched.IsOnline;

                matched.IsOnline = true;

                matched.LastSeen = DateTime.UtcNow;

                if (wasOffline)
                {
                    DeviceOnlineStatusChanged?.Invoke(matched);
                }
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);

                string json = Encoding.UTF8.GetString(result.Buffer);

                ProcessIncomingPacket(json, result.RemoteEndPoint);
            }

            catch (OperationCanceledException) { break; }

            catch (ObjectDisposedException) { break; }

            catch (Exception ex)
            {
                AppLogger.Warning($"Discovery receive error: {ex.Message}", "Discovery");
            }
        }
    }

    private void ProcessIncomingPacket(string json, IPEndPoint remoteEndPoint)
    {
        try
        {
            var packet = JsonSerializer.Deserialize<DiscoveryPacket>(json);

            if (packet == null || packet.App != "EdgeDrop" || string.IsNullOrWhiteSpace(packet.DeviceId))

                return;

            if (string.Equals(packet.DeviceId, _settings.DeviceId, StringComparison.OrdinalIgnoreCase))

                return;

            string senderIp = remoteEndPoint.Address.ToString();

            if (senderIp.StartsWith("::ffff:")) senderIp = senderIp.Substring(7);

            if (senderIp == "::1") senderIp = "127.0.0.1";

            if (packet.Type == "query")
            {
                _ = SendPacketAsync("beacon");
            }

            lock (_settings.DevicesLock)
            {
                if (packet.Type == "bye")
                {
                    var byeDev = _settings.Devices.FirstOrDefault(d =>

                        string.Equals(d.Id, packet.DeviceId, StringComparison.OrdinalIgnoreCase) ||

                        string.Equals(d.Ip?.Trim(), senderIp, StringComparison.OrdinalIgnoreCase));

                    if (byeDev != null && byeDev.IsOnline)
                    {
                        byeDev.IsOnline = false;

                        DeviceOnlineStatusChanged?.Invoke(byeDev);
                    }

                    return;
                }

                var matched = _settings.Devices.FirstOrDefault(d =>

                    string.Equals(d.Id, packet.DeviceId, StringComparison.OrdinalIgnoreCase));

                if (matched == null)
                {
                    matched = _settings.Devices.FirstOrDefault(d =>

                        string.Equals(d.Ip?.Trim(), senderIp, StringComparison.OrdinalIgnoreCase));

                    if (matched != null)
                    {
                        matched.Id = packet.DeviceId;
                    }
                }

                if (matched != null)
                {
                    bool ipChanged = !string.Equals(matched.Ip?.Trim(), senderIp, StringComparison.OrdinalIgnoreCase);

                    bool wasOffline = !matched.IsOnline;

                    if (ipChanged)
                    {
                        matched.Ip = senderIp;

                        matched.Port = packet.Port > 0 ? packet.Port : 11000;

                        _settings.Save();

                        DeviceIpUpdated?.Invoke(matched);
                    }

                    matched.IsOnline = true;

                    matched.LastSeen = DateTime.UtcNow;

                    if (wasOffline || ipChanged)
                    {
                        DeviceOnlineStatusChanged?.Invoke(matched);
                    }
                }

                else
                {
                    if (_settings.Devices.Count < 4)
                    {
                        var usedEdges = _settings.Devices.Select(d => d.DropEdge).ToHashSet();

                        if (_settings.WebServerEnabled) usedEdges.Add(_settings.WebDropEdge);

                        var allEdges = new[] { ScreenEdge.Right, ScreenEdge.Left, ScreenEdge.Top, ScreenEdge.Bottom };

                        var freeEdge = allEdges.FirstOrDefault(e => !usedEdges.Contains(e));

                        string devName = !string.IsNullOrWhiteSpace(packet.DeviceName)

                            ? packet.DeviceName.Trim()

                            : $"PC ({senderIp})";

                        var newDev = new DeviceItem
                        {
                            Id = packet.DeviceId,
                            Name = devName,
                            Ip = senderIp,
                            Port = packet.Port > 0 ? packet.Port : 11000,
                            DropEdge = freeEdge,
                            IsOnline = true,
                            LastSeen = DateTime.UtcNow,

                            IsCollapsed = false
                        };

                        _settings.AddKnownDevice(newDev);

                        NewDeviceDiscovered?.Invoke(newDev);
                    }
                }
            }
        }

        catch { }
    }

    private static DateTime _lastBroadcastRefresh = DateTime.MinValue;
    private static readonly List<IPAddress> _cachedBroadcasts = new();

    private static List<IPAddress> GetSubnetBroadcastAddresses()
    {
        if (DateTime.UtcNow - _lastBroadcastRefresh < TimeSpan.FromSeconds(30) && _cachedBroadcasts.Count > 0)
        {
            return _cachedBroadcasts;
        }

        var list = new List<IPAddress>();

        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up ||

                    iface.NetworkInterfaceType == NetworkInterfaceType.Loopback)

                    continue;

                foreach (var unicast in iface.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask != null)
                    {
                        try
                        {
                            byte[] ipBytes = unicast.Address.GetAddressBytes();

                            byte[] maskBytes = unicast.IPv4Mask.GetAddressBytes();

                            byte[] broadcastBytes = new byte[4];

                            for (int i = 0; i < 4; i++)
                            {
                                broadcastBytes[i] = (byte)(ipBytes[i] | (~maskBytes[i]));
                            }

                            list.Add(new IPAddress(broadcastBytes));
                        }

                        catch { }
                    }
                }
            }

            _cachedBroadcasts.Clear();

            _cachedBroadcasts.AddRange(list);

            _lastBroadcastRefresh = DateTime.UtcNow;
        }

        catch { }

        return _cachedBroadcasts;
    }

    private void SweepOfflineDevices()
    {
        var threshold = DateTime.UtcNow.AddSeconds(-20);

        lock (_settings.DevicesLock)
        {
            foreach (var dev in _settings.Devices)
            {
                if (dev.IsOnline && dev.LastSeen < threshold)
                {
                    dev.IsOnline = false;

                    DeviceOnlineStatusChanged?.Invoke(dev);
                }
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning) return;

            _isRunning = false;

            try
            {
                _ = SendPacketAsync("bye");
            }

            catch { }

            _beaconTimer?.Stop();

            _beaconTimer?.Dispose();

            _beaconTimer = null;

            _cts?.Cancel();

            try { _udpClient?.Close(); } catch { }

            try { _udpClient?.Dispose(); } catch { }

            _udpClient = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
