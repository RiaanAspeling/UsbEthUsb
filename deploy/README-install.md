# UsbEthUsb install notes

## Linux server

### Prerequisites

The kernel `usbip_host` module and the `usbip` / `usbipd` userspace tools. On Ubuntu/Debian:

```bash
sudo apt install linux-tools-generic hwdata
which usbip usbipd      # should resolve, typically /usr/bin/usbip and /usr/bin/usbipd
```

### Install as a service (starts automatically on every boot)

Two systemd units are involved — `usbipd.service` (the USB/IP daemon; also loads the
`usbip_host` module) and `usbethusb-server.service` (the .NET gRPC server, which depends on it).
The `install-server.sh` script handles the whole thing:

```bash
./publish.sh                 # build the server  ->  publish/server/
deploy/install-server.sh     # install + enable + start (auto-elevates with sudo)
```

`install-server.sh` copies the build to `/opt/usbethusb`, installs both systemd units, enables
and starts them, and opens TCP 5557 + 3240 in `ufw` if it is present. After a reboot the services
come back on their own, in order.

**Update** an existing install — rebuild and re-run (the script stops, replaces, and restarts):

```bash
./publish.sh && deploy/install-server.sh
```

**Uninstall** — removes both units and `/opt/usbethusb`:

```bash
deploy/uninstall-server.sh
```

Useful commands: `journalctl -u usbethusb-server -f` (live logs),
`systemctl status usbipd usbethusb-server`, `sudo systemctl restart usbethusb-server`.

Default gRPC port: **5557** (override in `appsettings.json`, or set `Server__Port` in the unit's
`[Service]` section). The server also runs a startup preflight that logs the state of the `usbip`
CLI, the kernel module, and `usbipd`.

## Windows client

### Prerequisite: install usbip-win2 first

The client cannot attach anything on its own — it drives **usbip-win2**, which supplies the `vhci`
virtual-USB driver and `usbip.exe`. Install it on the Windows machine **before** running the
client:

1. Download the latest installer (`USBip-<version>-x64.exe`) from the
   [**usbip-win2 releases page**](https://github.com/vadimgrn/usbip-win2/releases).
2. Run it. usbip-win2's `vhci` is a kernel driver — its own README covers any driver-signing /
   test-signing your Windows build requires.
3. Check it: run `usbip --version` in a terminal. If `usbip.exe` is not on `PATH`, the tray app's
   **Settings → Set usbip.exe path…** lets you point at it (the path is saved to config).

Without usbip-win2, attach fails — the client has no way to create the virtual USB device.

### Building the client

The client is a WinForms app and needs the `Microsoft.NET.Sdk.WindowsDesktop` MSBuild SDK.

- **On Windows**: the SDK from <https://dot.net> already includes it.
- **On Linux**: the Canonical `dotnet-sdk-8.0` APT package does **not** include it, and Microsoft
  no longer ships .NET to `packages.microsoft.com` for Ubuntu 24.04. Install the genuine Microsoft
  SDK to your home directory instead:

  ```bash
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$PATH"
  ```

  (Add the two `export` lines to `~/.bashrc` to persist them.)

Publish a self-contained Windows build (works from either OS once the SDK above is in place):

```bash
dotnet publish src/UsbEthUsb.Client/UsbEthUsb.Client.csproj \
  -c Release -r win-x64 --self-contained -o publish/client
```

The client runs as the invoking user — usbip-win2's attach/detach do not require elevation, so
there is no UAC prompt. Config lives at `%APPDATA%\UsbEthUsb\config.json`.

### Packaging a Windows installer

`deploy/windows-installer.iss` is an [Inno Setup](https://jrsoftware.org/isdl.php) script that
wraps the published client into a friendly `Setup.exe`. Build it **on Windows**:

1. Install **Inno Setup 6.3+**.
2. Publish the client to `publish\client\` (the command above).
3. Compile the script:

   ```
   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" deploy\windows-installer.iss
   ```

   (or open the `.iss` in the Inno Setup IDE and press Build).

This produces **`publish\UsbEthUsb-Client-Setup.exe`**. The installer:

- checks for usbip-win2 at startup and, if it isn't detected, offers to open the download page
  (a warning, not a hard block — a custom install path could be missed);
- is **per-user** — installs to `%LOCALAPPDATA%\Programs\UsbEthUsb`, no admin/UAC;
- adds a Start Menu shortcut, and optionally a "start at sign-in" entry (a checkbox in the wizard);
- closes a running instance automatically on upgrade (Restart Manager), so a locked `.exe` never
  blocks the update;
- registers a clean uninstaller (Settings → Apps, or the Start Menu entry).

User config in `%APPDATA%\UsbEthUsb\config.json` is left in place on uninstall.
