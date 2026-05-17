using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace UsbEthUsb.Server.Usbip;

/// Startup preflight: verifies the usbip CLI, the usbip-host kernel driver, and the usbipd
/// daemon — loading the module and starting the daemon when it can. It never aborts startup;
/// every problem is logged with the manual command to fix it.
public class UsbipEnvironmentCheck : IHostedService
{
    private const string DriverPath = "/sys/bus/usb/drivers/usbip-host";
    private const int UsbipdPort = 3240;

    private readonly ILogger<UsbipEnvironmentCheck> _logger;

    public UsbipEnvironmentCheck(ILogger<UsbipEnvironmentCheck> logger) => _logger = logger;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux())
        {
            _logger.LogWarning("usbip environment check skipped — server host is not Linux.");
            return;
        }

        try
        {
            await CheckCliAsync(ct);
            await EnsureModuleAsync(ct);
            await EnsureDaemonAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "usbip environment check failed unexpectedly; continuing anyway.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task CheckCliAsync(CancellationToken ct)
    {
        var (ok, output) = await RunAsync("usbip", new[] { "version" }, ct);
        if (ok)
            _logger.LogInformation("usbip CLI present: {Version}", output);
        else
            _logger.LogError(
                "usbip CLI not available ({Detail}). Install it, e.g. " +
                "'sudo apt install linux-tools-generic hwdata'.", output);
    }

    private async Task EnsureModuleAsync(CancellationToken ct)
    {
        if (Directory.Exists(DriverPath))
        {
            _logger.LogInformation("usbip-host kernel driver is loaded.");
            return;
        }

        _logger.LogWarning("usbip-host driver missing; attempting 'modprobe usbip_host'...");
        var (ok, output) = await RunAsync("/sbin/modprobe", new[] { "usbip_host" }, ct);

        if (ok && Directory.Exists(DriverPath))
        {
            _logger.LogInformation("Loaded the usbip_host kernel module.");
        }
        else
        {
            _logger.LogError(
                "Could not load the usbip_host kernel module ({Detail}). Device binding will " +
                "fail until it is loaded. Fix: 'sudo modprobe usbip_host' (and run this server " +
                "as root). Persist it with 'echo usbip_host | sudo tee /etc/modules-load.d/usbip.conf'.",
                output);
        }
    }

    private async Task EnsureDaemonAsync(CancellationToken ct)
    {
        if (await IsPortOpenAsync(UsbipdPort, ct))
        {
            _logger.LogInformation("usbipd is listening on TCP {Port}.", UsbipdPort);
            return;
        }

        _logger.LogWarning("usbipd is not listening on {Port}; attempting to start it...", UsbipdPort);
        // 'usbipd -D' forks a background daemon and the foreground process exits immediately.
        var (_, output) = await RunAsync("usbipd", new[] { "-D" }, ct);
        await Task.Delay(TimeSpan.FromMilliseconds(500), ct);

        if (await IsPortOpenAsync(UsbipdPort, ct))
        {
            _logger.LogInformation("Started usbipd; now listening on TCP {Port}.", UsbipdPort);
        }
        else
        {
            _logger.LogError(
                "usbipd is not running and could not be started ({Detail}). Clients will not be " +
                "able to attach devices. Fix: 'sudo usbipd -D', or " +
                "'sudo systemctl enable --now usbipd'.", output);
        }
    }

    private static async Task<bool> IsPortOpenAsync(int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(bool ok, string output)> RunAsync(string file, string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return (false, $"could not start {file}");

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(ct);
            var combined = $"{(await stdoutTask).Trim()} {(await stderrTask).Trim()}".Trim();

            return (p.ExitCode == 0, combined.Length > 0 ? combined : $"exit code {p.ExitCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
