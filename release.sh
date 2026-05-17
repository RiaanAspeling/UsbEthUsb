#!/usr/bin/env bash
#
# Streamlines a GitHub release. Builds the Linux server, packages it (with the
# install scripts, systemd units and a README) into a .tar.gz, and publishes a
# GitHub release with that tarball plus the Windows installer.
#
#   Usage:  ./release.sh <tag>            e.g.  ./release.sh v1.0.0
#
# Before running:
#   - Build the Windows installer on Windows (deploy/windows-installer.iss) and
#     copy it to  publish/UsbEthUsb-Client-Setup.exe
#   - For the upload step, install the GitHub CLI and sign in:
#       sudo apt install gh  &&  gh auth login
#     Without gh the script still builds the tarball and prints what to upload
#     by hand.
#
set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")"

TAG="${1:-}"
if [[ -z "$TAG" ]]; then
    echo "usage: ./release.sh <tag>   e.g.  ./release.sh v1.0.0" >&2
    exit 1
fi

REPO_SLUG="RiaanAspeling/UsbEthUsb"
INSTALLER="publish/UsbEthUsb-Client-Setup.exe"
STAGE="publish/release"
BUNDLE="usbethusb-server-${TAG}-linux-x64"
TARBALL="publish/${BUNDLE}.tar.gz"

# --- preconditions ---
if [[ ! -f "$INSTALLER" ]]; then
    echo "error: $INSTALLER not found." >&2
    echo "       Build the Windows installer on Windows and copy it there first" >&2
    echo "       (see deploy/README-install.md)." >&2
    exit 1
fi
if [[ -n "$(git status --porcelain)" ]]; then
    echo "warning: the working tree has uncommitted changes — the '$TAG' tag"
    echo "         will point at the current commit, without them."
    read -r -p "Continue anyway? [y/N] " reply
    [[ "$reply" =~ ^[Yy]$ ]] || exit 1
fi

# --- stash the installer: publish.sh wipes publish/ when it builds ---
STASH_DIR="$(mktemp -d)"
trap 'rm -rf "$STASH_DIR"' EXIT
cp "$INSTALLER" "$STASH_DIR/"

# --- build the server (this recreates publish/) ---
echo "==> Building the server..."
./publish.sh

# --- restore the installer alongside the fresh build ---
cp "$STASH_DIR/UsbEthUsb-Client-Setup.exe" "$INSTALLER"

# --- assemble the server tarball ---
echo "==> Packaging $TARBALL ..."
rm -rf "$STAGE"
mkdir -p "$STAGE/$BUNDLE/server"
cp -r publish/server/. "$STAGE/$BUNDLE/server/"
cp deploy/install-server.sh deploy/uninstall-server.sh \
   deploy/usbipd.service    deploy/usbethusb-server.service \
   "$STAGE/$BUNDLE/"
cp deploy/server-release-README.md "$STAGE/$BUNDLE/README.md"
chmod +x "$STAGE/$BUNDLE/install-server.sh" "$STAGE/$BUNDLE/uninstall-server.sh"
tar -czf "$TARBALL" -C "$STAGE" "$BUNDLE"
echo "    $(du -h "$TARBALL" | cut -f1)  $TARBALL"

# --- publish the GitHub release (or explain how to do it by hand) ---
NOTES="$(cat <<NOTES
### Downloads

**Windows client** — \`UsbEthUsb-Client-Setup.exe\`
Install [usbip-win2](https://github.com/vadimgrn/usbip-win2/releases) first (the
installer will prompt you if it is missing), then run the installer.

**Linux server** — \`${BUNDLE}.tar.gz\`
Extract it and run \`./install-server.sh\`. See the bundled README.
NOTES
)"

if command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
    echo "==> Creating GitHub release $TAG ..."
    gh release create "$TAG" "$TARBALL" "$INSTALLER" \
        --repo "$REPO_SLUG" \
        --title "UsbEthUsb $TAG" \
        --notes "$NOTES"
    echo "==> Published: https://github.com/$REPO_SLUG/releases/tag/$TAG"
else
    echo
    echo "==> gh CLI not available or not authenticated — release NOT published."
    echo "    Install it once:  sudo apt install gh  &&  gh auth login"
    echo "    Then re-run this script, or upload these two files by hand at"
    echo "    https://github.com/$REPO_SLUG/releases/new (tag $TAG):"
    echo "      - $TARBALL"
    echo "      - $INSTALLER"
fi
