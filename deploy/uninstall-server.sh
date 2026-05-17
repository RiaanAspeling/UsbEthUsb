#!/usr/bin/env bash
#
# Removes everything install-server.sh put in place: the two systemd units and
# /opt/usbethusb. Run from anywhere; needs root (re-runs itself under sudo).
#
# Left untouched: the usbip / usbipd packages, the usbip_host kernel module, and
# journald logs (those rotate on their own).
#
set -euo pipefail

SELF="$(readlink -f "$0")"
if [[ $EUID -ne 0 ]]; then
    exec sudo bash "$SELF" "$@"
fi

INSTALL_DIR="/opt/usbethusb"
UNIT_DIR="/etc/systemd/system"

echo "==> UsbEthUsb server uninstall"

echo "    stopping and disabling services..."
systemctl disable --now usbethusb-server.service 2>/dev/null || true
systemctl disable --now usbipd.service 2>/dev/null || true

echo "    removing systemd units..."
rm -f "$UNIT_DIR/usbethusb-server.service" "$UNIT_DIR/usbipd.service"
systemctl daemon-reload

echo "    removing $INSTALL_DIR..."
rm -rf "$INSTALL_DIR"

if command -v ufw >/dev/null 2>&1; then
    echo "    removing firewall rules 5557 + 3240 (ufw)..."
    ufw delete allow 5557/tcp >/dev/null 2>&1 || true
    ufw delete allow 3240/tcp >/dev/null 2>&1 || true
fi

echo
echo "==> Done. UsbEthUsb server removed."
echo "    usbip/usbipd and the usbip_host module were left in place."
