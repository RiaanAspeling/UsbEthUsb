using System.IO;
using System.Text.Json;

namespace UsbEthUsb.Client.Config;

public class ServerEntry
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 5557;
}

public class ClientConfig
{
    public List<ServerEntry> Servers { get; set; } = new();
    public List<string> AutoAttachBusIds { get; set; } = new();

    // Full path to usbip.exe (from usbip-win2). If null/empty, the executable is looked up on PATH.
    public string? UsbipExePath { get; set; }

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsbEthUsb", "config.json");

    public static ClientConfig LoadOrCreate()
    {
        var path = ConfigPath;
        if (!File.Exists(path))
        {
            var cfg = new ClientConfig();
            cfg.Save();
            return cfg;
        }
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ClientConfig>(json) ?? new ClientConfig();
        }
        catch
        {
            return new ClientConfig();
        }
    }

    public void Save()
    {
        var path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
