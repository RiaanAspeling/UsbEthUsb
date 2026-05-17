using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace UsbEthUsb.Client.Usbip;

public record UsbipResult(bool Ok, string Message, bool ExecutableMissing = false, int ExitCode = 0);

/// A USB device currently imported (attached) on this machine, per `usbip port`.
public record AttachedPort(int Port, string RemoteHost, string RemoteBusId);

public static class UsbipClientCli
{
    private const string DefaultExecutable = "usbip.exe";

    public static UsbipResult Attach(string? executablePath, string host, string busId) =>
        Run(executablePath, "attach", "-r", host, "-b", busId);

    public static UsbipResult Detach(string? executablePath, int port) =>
        Run(executablePath, "detach", "-p", port.ToString());

    /// Returns the devices currently attached on this machine. Empty if usbip.exe is
    /// unavailable or its output can't be parsed — callers treat that as "nothing attached".
    public static IReadOnlyList<AttachedPort> GetAttachedPorts(string? executablePath)
    {
        var result = Run(executablePath, "port");
        return result.Ok ? ParsePortList(result.Message) : Array.Empty<AttachedPort>();
    }

    // `usbip port` lists imported devices, e.g.:
    //   Port 00: <Port in Use> at High Speed(480Mbps)
    //          Logitech, Inc. : Webcam (046d:0825)
    //          3-1 -> usbip://192.168.1.42:3240/1-2
    private static readonly Regex PortRegex =
        new(@"^Port\s+(\d+):", RegexOptions.Compiled);
    private static readonly Regex UrlRegex =
        new(@"usbip://([^:/\s]+):\d+/(\S+)", RegexOptions.Compiled);

    public static List<AttachedPort> ParsePortList(string output)
    {
        var ports = new List<AttachedPort>();
        int? currentPort = null;
        foreach (var raw in output.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();

            var pm = PortRegex.Match(line);
            if (pm.Success)
            {
                currentPort = int.Parse(pm.Groups[1].Value);
                continue;
            }

            var um = UrlRegex.Match(line);
            if (um.Success && currentPort.HasValue)
            {
                ports.Add(new AttachedPort(currentPort.Value, um.Groups[1].Value, um.Groups[2].Value));
                currentPort = null;
            }
        }
        return ports;
    }

    private static UsbipResult Run(string? executablePath, params string[] args)
    {
        var exe = string.IsNullOrWhiteSpace(executablePath) ? DefaultExecutable : executablePath;
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return new UsbipResult(false, $"Failed to start {exe}");

            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            return p.ExitCode == 0
                ? new UsbipResult(true, stdout.Trim(), ExitCode: 0)
                : new UsbipResult(false,
                    string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim(),
                    ExitCode: p.ExitCode);
        }
        // ERROR_FILE_NOT_FOUND (2) or ERROR_PATH_NOT_FOUND (3) means the exe isn't where we looked.
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            return new UsbipResult(false, $"Executable not found: {exe}", ExecutableMissing: true);
        }
        catch (Exception ex)
        {
            return new UsbipResult(false, ex.Message);
        }
    }
}
