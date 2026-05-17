#!/usr/bin/env bash
#
# Publishes self-contained builds of both projects into ./publish/.
#   - Server -> linux-x64   (deploy to the Ubuntu host, see deploy/README-install.md)
#   - Client -> win-x64     (copy to the Windows machine)
#
# Override RIDs or configuration via environment variables:
#   SERVER_RID=linux-arm64 CLIENT_RID=win-arm64 CONFIG=Debug ./publish.sh
#
set -euo pipefail

# Always operate from the repo root, regardless of where the script is invoked.
cd "$(dirname "$(readlink -f "$0")")"

CONFIG="${CONFIG:-Release}"
SERVER_RID="${SERVER_RID:-linux-x64}"
CLIENT_RID="${CLIENT_RID:-win-x64}"
OUT_DIR="publish"

# Prefer the Microsoft SDK in ~/.dotnet (it has the WindowsDesktop targets the
# Client needs); fall back to whatever dotnet is on PATH.
if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    DOTNET="$HOME/.dotnet/dotnet"
    export DOTNET_ROOT="$HOME/.dotnet"
else
    DOTNET="$(command -v dotnet || true)"
fi
if [[ -z "${DOTNET:-}" ]]; then
    echo "error: no dotnet SDK found (looked in ~/.dotnet and PATH)" >&2
    exit 1
fi

echo "Using SDK: $("$DOTNET" --version)  ($DOTNET)"
echo "Cleaning $OUT_DIR/ ..."
rm -rf "$OUT_DIR"

publish() {
    local name="$1" project="$2" rid="$3" dest="$4"
    echo
    echo "==> Publishing $name  ($rid, $CONFIG)"
    "$DOTNET" publish "$project" \
        -c "$CONFIG" \
        -r "$rid" \
        --self-contained \
        -o "$dest"
}

publish "Server" "src/UsbEthUsb.Server/UsbEthUsb.Server.csproj" "$SERVER_RID" "$OUT_DIR/server"
publish "Client" "src/UsbEthUsb.Client/UsbEthUsb.Client.csproj" "$CLIENT_RID" "$OUT_DIR/client"

echo
echo "Done."
echo "  Server -> $OUT_DIR/server   ($SERVER_RID)"
echo "  Client -> $OUT_DIR/client   ($CLIENT_RID)"
