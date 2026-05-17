using UsbEthUsb.Client.Config;
using UsbEthUsb.Client.Services;
using UsbEthUsb.Client.Usbip;

namespace UsbEthUsb.Client;

public class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _appIcon;
    private readonly ClientConfig _config;
    private readonly List<ServerConnection> _connections = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private bool _refreshInFlight;

    public TrayAppContext()
    {
        _config = ClientConfig.LoadOrCreate();
        _appIcon = LoadAppIcon();

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "UsbEthUsb",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };

        foreach (var s in _config.Servers)
        {
            _connections.Add(new ServerConnection(s));
        }

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _refreshTimer.Tick += (_, _) => _ = RefreshMenuAsync();
        _refreshTimer.Start();

        _ = RefreshMenuAsync();
    }

    private async Task RefreshMenuAsync()
    {
        if (_refreshInFlight) return;
        _refreshInFlight = true;
        try
        {
            // What's attached on THIS machine right now — the source of truth for device state.
            // Runs off the UI thread because it spawns usbip.exe.
            var attached = await Task.Run(() => UsbipClientCli.GetAttachedPorts(_config.UsbipExePath));

            var menu = new ContextMenuStrip();
            // A right-click while we're rebuilding triggers an immediate background refresh,
            // so the *next* open shows fresh data even when the user clicks rapidly.
            menu.Opening += (_, _) => _ = RefreshMenuAsync();

            if (_connections.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("(no servers configured)") { Enabled = false });
            }

            foreach (var conn in _connections)
            {
                menu.Items.Add(await BuildServerItemAsync(conn, attached));
            }

            menu.Items.Add(new ToolStripSeparator());

            var refresh = new ToolStripMenuItem("Refresh now");
            refresh.Click += (_, _) => _ = RefreshMenuAsync();
            menu.Items.Add(refresh);

            var addServer = new ToolStripMenuItem("Add server...");
            addServer.Click += (_, _) => ShowAddServerDialog();
            menu.Items.Add(addServer);

            menu.Items.Add(BuildSettingsMenu());

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(BuildStamp()) { Enabled = false });
            var quit = new ToolStripMenuItem("Quit");
            quit.Click += (_, _) => ExitThread();
            menu.Items.Add(quit);

            var previous = _trayIcon.ContextMenuStrip;
            _trayIcon.ContextMenuStrip = menu;
            // Dispose the old menu (and its bold fonts) to avoid leaking GDI handles —
            // but only if it isn't currently on screen.
            if (previous is { Visible: false })
            {
                previous.Dispose();
            }
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    private async Task<ToolStripMenuItem> BuildServerItemAsync(
        ServerConnection conn, IReadOnlyList<AttachedPort> attached)
    {
        var serverItem = new ToolStripMenuItem(conn.DisplayName);
        try
        {
            var devices = await conn.ListDevicesAsync();
            if (devices.Count == 0)
            {
                serverItem.DropDownItems.Add(new ToolStripMenuItem("(no devices)") { Enabled = false });
            }
            foreach (var d in devices)
            {
                var port = FindAttachedPort(attached, conn.Config.Host, d.BusId);
                var isAttached = port is not null;

                var name = string.IsNullOrWhiteSpace(d.ProductName)
                    ? $"{d.VendorId:x4}:{d.ProductId:x4}"
                    : $"{d.VendorName} {d.ProductName}";
                var label = $"[{d.BusId}] {name}";

                // The device item is itself the toggle: checked + bold when attached,
                // a single click does the one action that makes sense.
                var devItem = new ToolStripMenuItem(label)
                {
                    Checked = isAttached,
                    ToolTipText = isAttached
                        ? $"Attached on local port {port!.Port} — click to detach"
                        : "Click to attach",
                };
                if (isAttached)
                {
                    devItem.Font = new Font(devItem.Font, FontStyle.Bold);
                }

                devItem.Click += async (_, _) =>
                {
                    if (port is not null)
                        await DetachAsync(conn, d.BusId, port.Port, label);
                    else
                        await AttachAsync(conn, d.BusId, label);
                };
                serverItem.DropDownItems.Add(devItem);
            }
        }
        catch (Exception ex)
        {
            serverItem.DropDownItems.Add(
                new ToolStripMenuItem($"(unreachable: {ex.Message})") { Enabled = false });
        }
        return serverItem;
    }

    // Matches an attached port to a configured server's device. Prefers an exact host+busid
    // match, then falls back to busid only — `usbip port` reports the host as an IP, which
    // won't string-match a server configured by hostname.
    private static AttachedPort? FindAttachedPort(
        IReadOnlyList<AttachedPort> attached, string serverHost, string busId)
    {
        return attached.FirstOrDefault(p =>
                   p.RemoteBusId == busId &&
                   string.Equals(p.RemoteHost, serverHost, StringComparison.OrdinalIgnoreCase))
               ?? attached.FirstOrDefault(p => p.RemoteBusId == busId);
    }

    private ToolStripMenuItem BuildSettingsMenu()
    {
        var settings = new ToolStripMenuItem("Settings");
        var setUsbipPath = new ToolStripMenuItem(
            string.IsNullOrEmpty(_config.UsbipExePath)
                ? "Set usbip.exe path..."
                : $"usbip.exe: {_config.UsbipExePath}");
        setUsbipPath.Click += (_, _) => PromptForUsbipPath();
        settings.DropDownItems.Add(setUsbipPath);

        if (!string.IsNullOrEmpty(_config.UsbipExePath))
        {
            var clear = new ToolStripMenuItem("Clear usbip.exe path (use PATH)");
            clear.Click += (_, _) =>
            {
                _config.UsbipExePath = null;
                _config.Save();
                _ = RefreshMenuAsync();
            };
            settings.DropDownItems.Add(clear);
        }
        return settings;
    }

    private bool PromptForUsbipPath()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Locate usbip.exe",
            Filter = "usbip executable (usbip.exe)|usbip.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            FileName = "usbip.exe",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return false;
        _config.UsbipExePath = dlg.FileName;
        _config.Save();
        _ = RefreshMenuAsync();
        return true;
    }

    private async Task AttachAsync(ServerConnection conn, string busId, string deviceLabel)
    {
        var confirm = MessageBox.Show(
            $"Attach this device?\n\n    {deviceLabel}\n\n" +
            "It will be taken over from the Linux host; any program using it there loses access.",
            "UsbEthUsb — Confirm attach",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        try
        {
            var (bound, bindMsg) = await conn.BindAsync(busId);
            if (!bound)
            {
                ShowFailed("Attach failed", deviceLabel, $"The host reported:\n{bindMsg}");
                return;
            }

            var local = await AttachLocallyAsync(conn, busId);
            if (local.Ok)
            {
                await RefreshMenuAsync();
                return;
            }

            // Bind succeeded but the device never attached here — release it on the host
            // so it isn't left orphaned (exported but used by nobody).
            var rolledBack = await TryUnbindAsync(conn, busId);
            var detail =
                $"usbip.exe could not attach the device (exit code {local.ExitCode}):\n\n" +
                CleanUsbipMessage(local.Message) +
                (rolledBack ? "\n\nThe device has been released on the host." : "");
            ShowFailed("Attach failed", deviceLabel, detail);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "UsbEthUsb", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Runs the local `usbip.exe attach`, handling the "exe not found -> locate it" flow.
    // Returns the final result (Ok, or the last failure).
    private async Task<UsbipResult> AttachLocallyAsync(ServerConnection conn, string busId)
    {
        var local = await Task.Run(() =>
            UsbipClientCli.Attach(_config.UsbipExePath, conn.Config.Host, busId));
        if (!local.ExecutableMissing) return local;

        var choice = MessageBox.Show(
            "usbip.exe was not found on PATH. Locate it now?",
            "UsbEthUsb", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (choice != DialogResult.Yes || !PromptForUsbipPath()) return local;

        return await Task.Run(() =>
            UsbipClientCli.Attach(_config.UsbipExePath, conn.Config.Host, busId));
    }

    private async Task DetachAsync(ServerConnection conn, string busId, int port, string deviceLabel)
    {
        var confirm = MessageBox.Show(
            $"Detach this device?\n\n    {deviceLabel}\n\n" +
            "Any program on this PC currently using it will lose access. " +
            "The device is released back to the Linux host.",
            "UsbEthUsb — Confirm detach",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        try
        {
            // 1. Detach locally — frees the vhci port and closes the import cleanly.
            var result = await Task.Run(() => UsbipClientCli.Detach(_config.UsbipExePath, port));
            if (!result.Ok)
            {
                ShowFailed("Detach failed", deviceLabel,
                    $"usbip.exe could not detach the device (exit code {result.ExitCode}):\n\n" +
                    CleanUsbipMessage(result.Message));
                return;
            }

            // 2. Unbind on the server so the device returns to the host and can be bound again.
            var (unbound, unbindMsg) = await conn.UnbindAsync(busId);
            if (!unbound)
            {
                ShowFailed("Detach — host not released", deviceLabel,
                    "The device was detached from this PC, but the host could not release it:\n\n" +
                    $"{unbindMsg}\n\nRe-attaching may fail until it is unbound. " +
                    $"On the host run:  sudo usbip unbind -b {busId}");
            }

            await RefreshMenuAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "UsbEthUsb", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task<bool> TryUnbindAsync(ServerConnection conn, string busId)
    {
        try
        {
            var (ok, _) = await conn.UnbindAsync(busId);
            return ok;
        }
        catch
        {
            return false;
        }
    }

    // Loads the embedded app.ico at the tray's preferred small-icon size.
    private static Icon LoadAppIcon()
    {
        try
        {
            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("UsbEthUsb.Client.app.ico");
            if (stream is not null)
                return new Icon(stream, SystemInformation.SmallIconSize);
        }
        catch
        {
            // fall through to a safe default
        }
        return new Icon(SystemIcons.Application, SystemInformation.SmallIconSize);
    }

    // Build time of the running assembly — lets you confirm at a glance which build is deployed.
    private static string BuildStamp()
    {
        try
        {
            var path = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(path)) path = Environment.ProcessPath ?? "";
            return $"Build: {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm}";
        }
        catch
        {
            return "Build: unknown";
        }
    }

    private static void ShowFailed(string title, string deviceLabel, string detail)
    {
        MessageBox.Show(
            $"{deviceLabel}\n\n{detail}",
            $"UsbEthUsb — {title}",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // usbip.exe stderr looks like "usbip: error: ...". Strip the noise for display.
    private static string CleanUsbipMessage(string raw)
    {
        var lines = raw.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l =>
            {
                foreach (var prefix in new[] { "usbip: error: ", "usbip: info: ", "usbip: " })
                {
                    if (l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return l[prefix.Length..];
                }
                return l;
            });
        var joined = string.Join(" ", lines).Trim();
        return joined.Length > 0 ? joined : "The usbip command failed with no detail.";
    }

    private void ShowAddServerDialog()
    {
        using var form = new Form
        {
            Text = "Add server",
            Width = 340,
            Height = 200,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
        };

        var nameLabel = new Label { Text = "Name",  Left = 10, Top = 15, Width = 60 };
        var nameBox   = new TextBox { Left = 80, Top = 12, Width = 230 };
        var hostLabel = new Label { Text = "Host",  Left = 10, Top = 45, Width = 60 };
        var hostBox   = new TextBox { Left = 80, Top = 42, Width = 230 };
        var portLabel = new Label { Text = "Port",  Left = 10, Top = 75, Width = 60 };
        var portBox   = new TextBox { Left = 80, Top = 72, Width = 230, Text = "5557" };

        var ok     = new Button { Text = "OK",     Left = 130, Top = 110, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 220, Top = 110, Width = 80, DialogResult = DialogResult.Cancel };

        form.Controls.AddRange(new Control[] { nameLabel, nameBox, hostLabel, hostBox, portLabel, portBox, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        if (form.ShowDialog() != DialogResult.OK) return;
        if (!int.TryParse(portBox.Text, out var port)) port = 5557;
        if (string.IsNullOrWhiteSpace(hostBox.Text)) return;

        var entry = new ServerEntry { Name = nameBox.Text, Host = hostBox.Text, Port = port };
        _config.Servers.Add(entry);
        _config.Save();
        _connections.Add(new ServerConnection(entry));
        _ = RefreshMenuAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _appIcon.Dispose();
            foreach (var c in _connections) c.Dispose();
        }
        base.Dispose(disposing);
    }
}
