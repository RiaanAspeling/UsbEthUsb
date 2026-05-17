#!/usr/bin/env bash
#
# Installs the UsbEthUsb server as systemd services so it starts on every boot.
# Installs two units: usbipd.service (the USB/IP daemon) and usbethusb-server.service.
#
# Build the server first with ./publish.sh, then run this from anywhere.
# Needs root — it re-runs itself under sudo if necessary.
# Re-run it after a fresh ./publish.sh to update an existing install.
#
set -euo pipefail

SELF="$(readlink -f "$0")"
if [[ $EUID -ne 0 ]]; then
    exec sudo bash "$SELF" "$@"
fi

SCRIPT_DIR="$(dirname "$SELF")"
INSTALL_DIR="/opt/usbethusb"
UNIT_DIR="/etc/systemd/system"

# Locate the published server. A release tarball keeps it at ./server next to
# this script; a repo checkout keeps it at ../publish/server.
if [[ -x "$SCRIPT_DIR/server/UsbEthUsb.Server" ]]; then
    PUBLISH="$SCRIPT_DIR/server"
else
    PUBLISH="$(dirname "$SCRIPT_DIR")/publish/server"
fi

echo "==> UsbEthUsb server install"

# --- the build must exist ---
if [[ ! -x "$PUBLISH/UsbEthUsb.Server" ]]; then
    echo "error: $PUBLISH/UsbEthUsb.Server not found." >&2
    echo "       Build the server first:  ./publish.sh" >&2
    exit 1
fi

# --- stop any running instance so files aren't in use ---
echo "    stopping any running instance..."
systemctl stop usbethusb-server.service 2>/dev/null || true
pkill -f "$INSTALL_DIR/UsbEthUsb.Server" 2>/dev/null || true

# --- install the binary (clean copy, so stale files don't linger on update) ---
echo "    installing binary to $INSTALL_DIR"
rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
cp -r "$PUBLISH"/. "$INSTALL_DIR"/
chmod +x "$INSTALL_DIR/UsbEthUsb.Server"

# --- install the systemd units ---
echo "    installing systemd units to $UNIT_DIR"
cp "$SCRIPT_DIR/usbipd.service" "$SCRIPT_DIR/usbethusb-server.service" "$UNIT_DIR"/
systemctl daemon-reload

# --- enable + start (enable = comes back after a reboot) ---
echo "    enabling and starting services..."
systemctl enable --now usbipd.service usbethusb-server.service

# --- firewall (best-effort) ---
if command -v ufw >/dev/null 2>&1; then
    echo "    opening firewall ports 5557 + 3240 (ufw)..."
    ufw allow 5557/tcp >/dev/null 2>&1 || true
    ufw allow 3240/tcp >/dev/null 2>&1 || true
else
    echo "    ufw not found — open TCP 5557 and 3240 manually if you run a firewall"
fi

echo
echo "==> Done. Service status:"
systemctl --no-pager --lines=0 status usbipd.service usbethusb-server.service || true
echo
echo "Live logs:  journalctl -u usbethusb-server -f"
