#!/usr/bin/env bash
# RixelPOS .NET SDK bootstrap for Linux (CachyOS/Arch, Debian/Ubuntu, Fedora, openSUSE).
# The RixelPOS app is published self-contained and does NOT need this at runtime.
# Use it for source builds, development or optional updates.
#
# Usage:
#   ./install-dotnet.sh [8.0|10.0]
#   DOTNET_CHANNEL=10.0 ./install-dotnet.sh
set -euo pipefail

CHANNEL="${1:-${DOTNET_CHANNEL:-8.0}}"
case "$CHANNEL" in
  8.0|8|10.0|10) ;;
  *) echo "Unsupported channel '$CHANNEL'. Use 8.0 or 10.0." >&2; exit 2 ;;
esac

if command -v dotnet >/dev/null 2>&1; then
  if dotnet --list-sdks 2>/dev/null | grep -Eq '^(8|10)\.'; then
    echo "==> .NET SDK already installed:"
    dotnet --list-sdks | sed 's/^/    /'
    exit 0
  fi
fi

SUDO=""
if [ "$(id -u)" -ne 0 ]; then
  if command -v sudo >/dev/null 2>&1; then SUDO="sudo"; else
    echo "!! sudo is not available. Run this script as root or install sudo." >&2
    exit 1
  fi
fi

echo "==> Installing .NET SDK channel $CHANNEL"

if command -v pacman >/dev/null 2>&1; then
  # CachyOS / Arch. dotnet-sdk is the current supported SDK (8 or newer).
  $SUDO pacman -Sy --needed --noconfirm dotnet-sdk || true
elif command -v apt-get >/dev/null 2>&1; then
  $SUDO apt-get update
  if [ "$CHANNEL" = "10.0" ]; then SDK_PKG="dotnet-sdk-10.0"; else SDK_PKG="dotnet-sdk-8.0"; fi
  DEBIAN_FRONTEND=noninteractive $SUDO apt-get install -y "$SDK_PKG" || true
elif command -v dnf >/dev/null 2>&1; then
  if [ "$CHANNEL" = "10.0" ]; then SDK_PKG="dotnet-sdk-10.0"; else SDK_PKG="dotnet-sdk-8.0"; fi
  $SUDO dnf install -y "$SDK_PKG" || $SUDO dnf install -y dotnet-sdk
elif command -v zypper >/dev/null 2>&1; then
  if [ "$CHANNEL" = "10.0" ]; then SDK_PKG="dotnet-sdk-10.0"; else SDK_PKG="dotnet-sdk-8.0"; fi
  $SUDO zypper --non-interactive install "$SDK_PKG" || $SUDO zypper --non-interactive install dotnet-sdk
fi

if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -Eq '^(8|10)\.'; then
  echo "==> .NET SDK installed:"
  dotnet --list-sdks | sed 's/^/    /'
  exit 0
fi

echo "==> Package manager did not provide an SDK. Falling back to dotnet-install.sh."
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$TMP/dotnet-install.sh"
chmod +x "$TMP/dotnet-install.sh"

if [ "$(id -u)" -eq 0 ]; then
  INSTALL_DIR="${DOTNET_INSTALL_DIR:-/usr/share/dotnet}"
  "$TMP/dotnet-install.sh" --channel "$CHANNEL" --install-dir "$INSTALL_DIR" --no-path
  ln -sf "$INSTALL_DIR/dotnet" /usr/local/bin/dotnet
else
  INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
  "$TMP/dotnet-install.sh" --channel "$CHANNEL" --install-dir "$INSTALL_DIR" --no-path
  PROFILE="$HOME/.profile"
  MARK="# RixelPOS dotnet"
  if ! grep -q "$MARK" "$PROFILE" 2>/dev/null; then
    printf '\n%s\nexport DOTNET_ROOT="%s"\nexport PATH="%s:$PATH"\n' "$MARK" "$INSTALL_DIR" "$INSTALL_DIR" >> "$PROFILE"
  fi
  export DOTNET_ROOT="$INSTALL_DIR"
  export PATH="$INSTALL_DIR:$PATH"
fi

if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks | grep -Eq '^(8|10)\.'; then
  echo "==> .NET SDK ready:"
  dotnet --list-sdks | sed 's/^/    /'
  echo "    Re-open your terminal (or source ~/.profile) if 'dotnet' is not found."
else
  echo "!! Installation could not be verified. Install the SDK manually from https://dotnet.microsoft.com/download" >&2
  exit 1
fi
