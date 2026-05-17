using System.Diagnostics;
using System.Text.RegularExpressions;
using UsbEthUsb.Protocol;

namespace UsbEthUsb.Server.Usbip;

public class UsbipCli
{
    private const string Executable = "usbip";
    private readonly ILogger<UsbipCli> _logger;

    public UsbipCli(ILogger<UsbipCli> logger) => _logger = logger;

    public async Task<List<Device>> ListLocalDevicesAsync(CancellationToken ct)
    {
        var r = await RunAsync(new[] { "list", "-l" }, ct);
        if (!r.Ok)
        {
            _logger.LogWarning("usbip list failed: {Error}", r.FriendlyError("unknown error"));
            return new List<Device>();
        }
        return ParseLocalList(r.Stdout);
    }

    public async Task<(bool ok, string message)> BindAsync(string busId, CancellationToken ct)
    {
        var r = await RunAsync(new[] { "bind", "-b", busId }, ct);
        if (r.Ok)
            return (true, "Device exported on the host.");

        // "already bound to usbip-host" means the device is already exported — which is exactly
        // what a bind aims for — so treat it as success and let the client proceed to attach.
        var detail = r.FriendlyError("usbip bind failed.");
        if (detail.Contains("already bound", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Device {BusId} was already exported on the host.", busId);
            return (true, "Device was already exported on the host.");
        }

        _logger.LogWarning("usbip bind -b {BusId} failed: {Detail}", busId, detail);
        return (false, detail);
    }

    public async Task<(bool ok, string message)> UnbindAsync(string busId, CancellationToken ct)
    {
        var r = await RunAsync(new[] { "unbind", "-b", busId }, ct);
        if (r.Ok)
            return (true, "Device released on the host.");

        // If the device isn't bound, the desired end state (not exported) is already met.
        var detail = r.FriendlyError("usbip unbind failed.");
        if (detail.Contains("not bound", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("not in match", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Device {BusId} was not exported on the host.", busId);
            return (true, "Device was not exported on the host.");
        }

        _logger.LogWarning("usbip unbind -b {BusId} failed: {Detail}", busId, detail);
        return (false, detail);
    }

    private async Task<CliResult> RunAsync(string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(Executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return new CliResult(false, "", "", "could not start usbip");

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            // Don't log here — a non-zero exit isn't necessarily a problem (e.g. "already
            // bound"). The caller decides whether it's a warning or benign and logs accordingly.
            return new CliResult(p.ExitCode == 0, stdout, stderr);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invoke usbip");
            return new CliResult(false, "", "", ex.Message);
        }
    }

    private sealed record CliResult(bool Ok, string Stdout, string Stderr, string? Exception = null)
    {
        // Produces a single readable line from whatever the CLI gave us, stripping the
        // "usbip: error: " noise so the client can show it verbatim.
        public string FriendlyError(string fallback)
        {
            var raw = FirstNonEmpty(Stderr, Exception, Stdout) ?? fallback;
            var lines = raw.Replace("\r\n", "\n").Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Select(StripUsbipPrefix);
            var joined = string.Join(" ", lines).Trim();
            return joined.Length > 0 ? joined : fallback;
        }

        private static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        private static string StripUsbipPrefix(string line)
        {
            foreach (var prefix in new[] { "usbip: error: ", "usbip: info: ", "usbip: " })
            {
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return line[prefix.Length..];
            }
            return line;
        }
    }

    // Parses `usbip list -l` output, e.g.:
    //  - busid 1-2 (046d:c52b)
    //    Logitech, Inc. : USB Receiver (046d:c52b)
    private static readonly Regex BusidRegex =
        new(@"^\s*-\s*busid\s+(\S+)\s+\(([0-9a-fA-F]{4}):([0-9a-fA-F]{4})\)", RegexOptions.Compiled);

    private static List<Device> ParseLocalList(string output)
    {
        var devices = new List<Device>();
        var lines = output.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var m = BusidRegex.Match(lines[i]);
            if (!m.Success) continue;

            var device = new Device
            {
                BusId = m.Groups[1].Value,
                VendorId = Convert.ToUInt32(m.Groups[2].Value, 16),
                ProductId = Convert.ToUInt32(m.Groups[3].Value, 16),
                IsExported = false,
            };

            if (i + 1 < lines.Length)
            {
                var info = lines[i + 1].Trim();
                var parts = info.Split(':', 2);
                if (parts.Length == 2)
                {
                    device.VendorName = parts[0].Trim();
                    var productPart = parts[1].Trim();
                    var paren = productPart.LastIndexOf('(');
                    device.ProductName = paren > 0 ? productPart[..paren].Trim() : productPart;
                }
            }
            devices.Add(device);
        }
        return devices;
    }
}
