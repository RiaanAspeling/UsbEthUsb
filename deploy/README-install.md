# UsbEthUsb install notes

## Linux server

Requires kernel `usbip_host` module and the `usbip` userspace tool. On Ubuntu/Debian:

```bash
sudo apt install linux-tools-generic hwdata
sudo modprobe usbip_host
which usbip   # should resolve, typically /usr/bin/usbip
```

Build and deploy the server:

```bash
dotnet publish src/UsbEthUsb.Server -c Release -r linux-x64 --self-contained -o publish/server
sudo mkdir -p /opt/usbethusb
sudo cp -r publish/server/* /opt/usbethusb/
sudo cp deploy/usbethusb-server.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now usbethusb-server
sudo systemctl status usbethusb-server
```

Default gRPC port: **5557** (configure in `appsettings.json` or `Server__Port` env var).

Open the firewall if needed:

```bash
sudo ufw allow 5557/tcp
```

At startup the server runs a preflight check (logged at `Information`/`Error` level): it verifies
the `usbip` CLI, loads the `usbip_host` kernel module if missing, and starts `usbipd` if it isn't
already listening on TCP **3240**. The module load and daemon start only succeed when the server
runs **as root** (the systemd unit does). If you run it unprivileged for development, do these
once yourself:

```bash
sudo modprobe usbip_host
echo usbip_host | sudo tee /etc/modules-load.d/usbip.conf   # persist across reboots
sudo usbipd -D                                              # or: sudo systemctl enable --now usbipd
```

`usbipd` listens on TCP **3240**. That port also needs to be open to the LAN.

## Windows client

Requires [usbip-win2](https://github.com/vadimgrn/usbip-win2) installed (provides the `vhci` driver
and `usbip.exe`). If `usbip.exe` is not on `PATH`, the tray app's
**Settings → Set usbip.exe path…** lets you point at it; the path is saved to config.

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

Copy `publish/client/` to the Windows machine and run `UsbEthUsb.Client.exe`. It runs as the
invoking user — usbip-win2's attach/detach do not require elevation, so there is no UAC prompt.
Auto-start at login can be added via a shortcut in the user's Startup folder or a
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry entry — neither is configured
automatically in v1.

Config file lives at `%APPDATA%\UsbEthUsb\config.json`.
