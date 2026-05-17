# UsbEthUsb — Linux server

Exposes USB devices on this Linux host over the network, so the UsbEthUsb
Windows client can attach them as if they were plugged in locally.

Project: https://github.com/RiaanAspeling/UsbEthUsb

## Requirements

- A Linux host with systemd (e.g. Ubuntu).
- The USB/IP userspace tools and kernel module:

      sudo apt install linux-tools-generic hwdata      # Ubuntu / Debian

## Install

From inside this extracted folder:

    ./install-server.sh

The script asks for sudo, then:

  - copies the server to /opt/usbethusb
  - installs and enables two systemd services — usbipd and usbethusb-server —
    so they start automatically on every boot
  - opens TCP 5557 (gRPC control) and 3240 (USB/IP data) in ufw, if present

Verify:

    systemctl status usbipd usbethusb-server
    journalctl -u usbethusb-server -f          # live logs

## Update

Download the newer release, extract it, and run ./install-server.sh again.

## Uninstall

    ./uninstall-server.sh

Removes the services and /opt/usbethusb. The usbip packages and the kernel
module are left in place.

## Notes

- The server binary is self-contained — no .NET runtime needs to be installed.
- The gRPC control port (default 5557) can be changed in
  /opt/usbethusb/appsettings.json.
- Point the Windows client at this machine's IP address, port 5557.
