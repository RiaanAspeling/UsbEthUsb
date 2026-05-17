# UsbEthUsb

A .NET orchestrator over [USB/IP](https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html)
that lets you expose a USB device on a Linux host and consume it on a Windows client as if it were
plugged in locally.

This project does **not** reimplement USB/IP or the Windows kernel driver. It drives:

- `usbip` userspace tools + `usbip_host` kernel module on Linux (server side)
- [`usbip-win2`](https://github.com/vadimgrn/usbip-win2)'s `vhci` driver + `usbip.exe` on Windows
  (client side)

The .NET code is the control plane: device discovery, bind/unbind, attach/detach, and the tray UI.
The actual USB/IP traffic flows kernel-to-kernel between the two machines on TCP/3240, untouched
by this app.

## Requirements

**Linux server** — the `usbip`/`usbipd` userspace tools and the `usbip_host` kernel module
(`sudo apt install linux-tools-generic hwdata` on Ubuntu/Debian).

**Windows client** — [**usbip-win2**](https://github.com/vadimgrn/usbip-win2) **must be installed
before the client can attach anything.** It supplies the `vhci` virtual-USB driver and `usbip.exe`,
which the client drives. Download the latest installer (`USBip-<version>-x64.exe`) from the
[**usbip-win2 releases page**](https://github.com/vadimgrn/usbip-win2/releases) and run it — its
README covers any Windows driver-signing steps. Without it, attach fails: the client has no way to
create the virtual USB device.

## Layout

```
UsbEthUsb.sln
├── src/
│   ├── UsbEthUsb.Shared/        # owns the gRPC schema (.proto)
│   ├── UsbEthUsb.Server/        # Linux daemon — ASP.NET Core gRPC on :5557
│   └── UsbEthUsb.Client/        # Windows tray app (WinForms, net8.0-windows)
└── deploy/                       # install scripts, systemd units, Windows installer
```

`UsbEthUsb.Shared` is a tiny library that owns `Protos/UsbDeviceService.proto`. Both the Server
(`GrpcServices=Server`) and the Client (`GrpcServices=Client`) link to that same `.proto` file,
which avoids a transitive `Microsoft.AspNetCore.App` framework reference leaking into the
WinForms client.

## Build

### SDK requirement

Use **Microsoft's** .NET 8 SDK, not a distro-packaged one. The `UsbEthUsb.Client` project is a
WinForms app (`net8.0-windows`) and needs the `Microsoft.NET.Sdk.WindowsDesktop` MSBuild SDK.
Canonical's `dotnet-sdk-8.0` package on Ubuntu 22.04+ **strips that SDK out**, and Microsoft no
longer publishes .NET to `packages.microsoft.com` for Ubuntu 24.04 — so APT cannot give you a
working SDK for this project.

On Linux, install the genuine Microsoft SDK to your home directory:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"

# Make it lead PATH for current + future shells
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
echo 'export DOTNET_ROOT="$HOME/.dotnet"' >> ~/.bashrc
echo 'export PATH="$HOME/.dotnet:$PATH"'  >> ~/.bashrc

# Sanity check — this must list Microsoft.NET.Sdk.WindowsDesktop
ls "$HOME/.dotnet/sdk/"*"/Sdks/" | grep WindowsDesktop
```

This shadows any distro `dotnet`; it does not uninstall it. To undo, `rm -rf ~/.dotnet` and
remove the two `~/.bashrc` lines. On Windows the SDK from <https://dot.net> already includes the
WindowsDesktop SDK — no extra steps.

### Building

Server and Shared build on any OS with the SDK above. The Client builds anywhere too once the
WindowsDesktop SDK is present (`EnableWindowsTargeting=true` is set in its csproj):

```bash
dotnet build UsbEthUsb.sln                          # whole solution
dotnet build src/UsbEthUsb.Server/UsbEthUsb.Server.csproj
dotnet build src/UsbEthUsb.Client/UsbEthUsb.Client.csproj
```

To produce a runnable Windows executable for the client:

```bash
dotnet publish src/UsbEthUsb.Client/UsbEthUsb.Client.csproj \
  -c Release -r win-x64 --self-contained -o publish/client
```

Or run `./publish.sh` to publish both the server (`publish/server/`, linux-x64) and the client
(`publish/client/`, win-x64) in one step.

## Install

**Pre-built downloads** are on the
[Releases page](https://github.com/RiaanAspeling/UsbEthUsb/releases) — the Windows installer and a
Linux server tarball, each with install instructions. The steps below build from source instead;
full detail (firewall, troubleshooting, SDK setup) is in
[`deploy/README-install.md`](deploy/README-install.md). The short version:

### Linux server

```bash
./publish.sh                 # builds publish/server/
deploy/install-server.sh     # installs + enables the systemd services (auto-elevates via sudo)
```

This installs two units — `usbipd.service` and `usbethusb-server.service` — and enables them so
they start on every boot, and opens TCP 5557 + 3240 in `ufw`. Re-run both commands to update;
`deploy/uninstall-server.sh` removes everything.

### Windows client

Install [**usbip-win2**](https://github.com/vadimgrn/usbip-win2/releases) first (see
[Requirements](#requirements)). Then build the installer — on Windows, with
[Inno Setup 6.3+](https://jrsoftware.org/isdl.php) installed:

```
dotnet publish src\UsbEthUsb.Client\UsbEthUsb.Client.csproj -c Release -r win-x64 --self-contained -o publish\client
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" deploy\windows-installer.iss
```

Run the resulting **`publish\UsbEthUsb-Client-Setup.exe`** — a per-user install (no admin) that
adds a Start Menu shortcut and an optional "start at sign-in" entry. Or skip the installer
entirely: copy `publish\client\` anywhere and run `UsbEthUsb.Client.exe`.

## Releasing (maintainers)

1. Build the Windows installer on Windows (`deploy/windows-installer.iss`) and copy it to
   `publish/UsbEthUsb-Client-Setup.exe`.
2. From the repo root: `./release.sh v1.2.3`

`release.sh` builds the server, packages it into `usbethusb-server-<tag>-linux-x64.tar.gz`
(bundling the install scripts, systemd units and a README), and publishes a GitHub release with
both that tarball and the installer. It uses the [GitHub CLI](https://cli.github.com)
(`sudo apt install gh && gh auth login`); without `gh` it still builds the tarball and prints the
manual-upload steps.

## v1 scope

What's wired up:

- gRPC server with `ListDevices`, `BindDevice`, `UnbindDevice`, `StreamEvents` (keepalive only).
- `usbip list -l` / `usbip bind` / `usbip unbind` shelled out from the server.
- Tray icon on Windows with: server list and per-server devices. Each device is a checkable
  toggle — **bold + checked when attached to this machine**, click to attach/detach. Attached
  state is read live from `usbip port`.
- Attach and detach both fully wired (`usbip detach -p <port>`, port resolved from `usbip port`).
- Config persisted to `%APPDATA%\UsbEthUsb\config.json`.

What's stubbed / not done yet:

- **Hotplug events** — `StreamEvents` is a keepalive. Real implementation needs netlink/udev on
  Linux to push events as devices appear/disappear.
- **Server-side exported state** — `Device.is_exported` is always `false`; reading the live bind
  state needs inspection of `/sys/bus/usb/drivers/usbip-host/`. (The client doesn't depend on
  this — it derives attached state from `usbip port` locally.)
- **Auto-attach on reconnect** — config has the field, no logic yet.
- **Auth / TLS** — plaintext HTTP/2 on the LAN. Designed-for, not built.
- **mDNS discovery** — manual host/port entry only.

## Test plan for v1

1. Run the server on Ubuntu: `dotnet run --project src/UsbEthUsb.Server` (as root, so it can call
   `usbip bind`).
2. Verify `curl http://localhost:5557/` shows the placeholder message.
3. On Windows, build and run the Client. Add a server pointing at the Ubuntu host's IP, port 5557.
4. Open the tray menu — devices from the host's `usbip list -l` should appear under the server.
5. Click Attach on a webcam — the server runs `usbip bind`, the Client runs
   `usbip.exe attach -r <host> -b <busid>`, and the webcam should appear in Windows Device Manager.

## Security note

v1 is plaintext, unauthenticated gRPC on the LAN. Treat it like USB/IP itself: do not expose
either port to untrusted networks. TLS + token auth is on the v2 list.
