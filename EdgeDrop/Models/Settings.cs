using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdgeDrop;

public enum ScreenEdge
{
    Right,
    Left,
    Top,
    Bottom
}

public enum AppTheme
{
    Dark,
    Light,
    Custom
}

public class KnownDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "PC";
    public string Ip { get; set; } = "192.168.1.2";
}

public class DeviceItem : KnownDevice
{
    public int Port { get; set; } = 11000;
    public ScreenEdge DropEdge { get; set; } = ScreenEdge.Right;
    public bool IsCollapsed { get; set; } = false;
    [JsonIgnore]
    public bool IsOnline { get; set; } = false;
    [JsonIgnore]
    public DateTime LastSeen { get; set; } = DateTime.MinValue;
}

public class Settings
{
    public string PeerIp { get; set; } = "192.168.1.2";
    public int Port { get; set; } = 11000;
    public bool LoopbackMode { get; set; } = false;
    public ScreenEdge DropEdge { get; set; } = ScreenEdge.Right;

    public List<DeviceItem> Devices { get; set; } = new()
    {
        new DeviceItem { Name = "PC 1", Ip = "192.168.1.2", Port = 11000, DropEdge = ScreenEdge.Right }
    };

    [JsonIgnore]
    private readonly object _devicesLock = new();
    [JsonIgnore]
    public object DevicesLock => _devicesLock;

    public IReadOnlyList<DeviceItem> GetDevicesSnapshot()
    {
        lock (_devicesLock)
        {
            return Devices.ToList();
        }
    }

    [JsonIgnore]
    public IReadOnlyList<DeviceItem> KnownDevices => GetDevicesSnapshot();
    public bool NotificationsEnabled { get; set; } = true;
    public bool RequireAcceptIncoming { get; set; } = true;
    public int SpeedLimitKBps { get; set; } = 0;
    public long MaxIncomingFileSizeMB { get; set; } = 0;
    public bool AutoStartServicesOnLaunch { get; set; } = true;
    public bool SafeModeEnabled { get; set; } = true;
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceName { get; set; } = Environment.MachineName;
    public bool AutoDiscoveryEnabled { get; set; } = true;
    public int DiscoveryPort { get; set; } = 11001;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public string BackgroundColorHex { get; set; } = "#202020";
    public string AccentColorHex { get; set; } = "#0078D4";
    public AppLanguage Language { get; set; } = AppLanguage.System;

    public string SaveFolder { get; set; } =

        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "EdgeDrop");

    public int WebPort { get; set; } = 8080;
    public bool WebServerEnabled { get; set; } = false;
    public bool WebCardCollapsed { get; set; } = false;
    public ScreenEdge WebDropEdge { get; set; } = ScreenEdge.Bottom;
    public Dictionary<string, string> SavedDeviceNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetSavedDeviceName(string? ip, string fallbackName)
    {
        lock (_devicesLock)
        {
            if (!string.IsNullOrWhiteSpace(ip) && SavedDeviceNames.TryGetValue(ip.Trim(), out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            if (!string.IsNullOrWhiteSpace(fallbackName) && SavedDeviceNames.TryGetValue(fallbackName.Trim(), out var byName) && !string.IsNullOrWhiteSpace(byName))
            {
                return byName;
            }

            return fallbackName;
        }
    }

    public void SetSavedDeviceName(string? ip, string? defaultName, string customName)
    {
        if (string.IsNullOrWhiteSpace(customName)) return;

        lock (_devicesLock)
        {
            if (!string.IsNullOrWhiteSpace(ip))
            {
                SavedDeviceNames[ip.Trim()] = customName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(defaultName))
            {
                SavedDeviceNames[defaultName.Trim()] = customName.Trim();
            }

            Save();
        }
    }

    private static readonly string NewConfigDir =

        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgeDrop");

    private static readonly string ConfigPath =

        Path.Combine(NewConfigDir, "settings.json");

    private static readonly string OldConfigPath =

        Path.Combine(AppContext.BaseDirectory, "settings.json");

    public void AddKnownDevice(DeviceItem device)
    {
        if (device == null) return;

        lock (_devicesLock)
        {
            if (!Devices.Any(d => string.Equals(d.Ip?.Trim(), device.Ip?.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                Devices.Add(device);

                Save();
            }
        }
    }

    public void AddKnownDevice(string name, string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return;

        lock (_devicesLock)
        {
            if (!Devices.Any(d => string.Equals(d.Ip?.Trim(), ip.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                Devices.Add(new DeviceItem
                {
                    Name = string.IsNullOrWhiteSpace(name) ? "PC" : name.Trim(),
                    Ip = ip.Trim(),
                    Port = 11000,

                    DropEdge = ScreenEdge.Right
                });

                Save();
            }
        }
    }

    public bool RemoveKnownDevice(string idOrIp)
    {
        if (string.IsNullOrWhiteSpace(idOrIp)) return false;

        lock (_devicesLock)
        {
            int removed = Devices.RemoveAll(d =>

                string.Equals(d.Id, idOrIp, StringComparison.OrdinalIgnoreCase) ||

                string.Equals(d.Ip?.Trim(), idOrIp.Trim(), StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                Save();

                return true;
            }

            return false;
        }
    }

    public bool ContainsKnownDevice(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;

        var cleanIp = ip.Trim();

        if (cleanIp.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))

            cleanIp = cleanIp.Substring(7);

        if (LoopbackMode && (cleanIp == "127.0.0.1" || cleanIp == "::1" || cleanIp.Equals("localhost", StringComparison.OrdinalIgnoreCase)))

            return true;

        lock (_devicesLock)
        {
            if (Devices.Count == 0 && !string.IsNullOrWhiteSpace(PeerIp))
            {
                var fallbackIp = PeerIp.Trim();

                if (fallbackIp.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))

                    fallbackIp = fallbackIp.Substring(7);

                if (string.Equals(fallbackIp, cleanIp, StringComparison.OrdinalIgnoreCase))

                    return true;
            }

            return Devices.Any(d =>
            {
                var devIp = d.Ip?.Trim() ?? "";

                if (devIp.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))

                    devIp = devIp.Substring(7);

                return string.Equals(devIp, cleanIp, StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    public static Settings Load()
    {
        try
        {
            string? pathToRead = null;

            bool needMigration = false;

            if (File.Exists(ConfigPath))
            {
                pathToRead = ConfigPath;
            }

            else if (File.Exists(OldConfigPath))
            {
                pathToRead = OldConfigPath;

                needMigration = true;
            }

            if (pathToRead != null)
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(pathToRead)) ?? new Settings();

                if (s.Devices == null || s.Devices.Count == 0)
                {
                    s.Devices = new List<DeviceItem>
                    {
                        new DeviceItem
                        {
                            Name = "PC 1",
                            Ip = !string.IsNullOrWhiteSpace(s.PeerIp) ? s.PeerIp : "192.168.1.2",
                            Port = s.Port > 0 ? s.Port : 11000,
                            DropEdge = s.DropEdge,

                            IsCollapsed = false
                        }
                    };
                }

                if (s.WebPort <= 0 || s.WebPort > 65535) s.WebPort = 8080;

                if (needMigration)
                {
                    s.Save();
                }

                return s;
            }
        }

        catch { }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(NewConfigDir);

            string tempFile = Path.Combine(NewConfigDir, $"settings.{Guid.NewGuid():N}.tmp");

            string json;

            lock (_devicesLock)
            {
                json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            }

            File.WriteAllText(tempFile, json);

            if (File.Exists(ConfigPath))
            {
                File.Replace(tempFile, ConfigPath, null);
            }

            else
            {
                File.Move(tempFile, ConfigPath, overwrite: true);
            }
        }

        catch { }
    }
}
