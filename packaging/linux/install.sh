#!/usr/bin/env bash
# Installs the portable RixelPOS folder into the current user profile (or system-wide).
# The app is self-contained; .NET is optional and only useful for source builds.
#
# Usage:
#   ./install.sh [--system] [--with-dotnet] [--prefix DIR]
set -euo pipefail

SOURCE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SYSTEM=0
WITH_DOTNET=0
PREFIX=""

while [ $# -gt 0 ]; do
  case "$1" in
    --system) SYSTEM=1 ;;
    --with-dotnet) WITH_DOTNET=1 ;;
    --prefix) PREFIX="${2:-}"; shift ;;
    -h|--help)
      grep '^#' "$0" | sed 's/^# \{0,1\}//' | head -20
      exit 0
      ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done

if [ ! -x "$SOURCE/RixelPOS" ]; then
  echo "!! RixelPOS executable not found next to this script." >&2
  exit 1
fi

if [ "$SYSTEM" -eq 1 ]; then
  [ "$(id -u)" -eq 0 ] || { echo "!! --system requires root (sudo)." >&2; exit 1; }
  DEST="${PREFIX:-/opt/rixelpos}"
  BIN="/usr/local/bin/rixelpos"
  APPS="/usr/share/applications"
  ICONS="/usr/share/icons/hicolor/256x256/apps"
else
  DEST="${PREFIX:-$HOME/.local/opt/rixelpos}"
  BIN="$HOME/.local/bin/rixelpos"
  APPS="$HOME/.local/share/applications"
  ICONS="$HOME/.local/share/icons/hicolor/256x256/apps"
fi

echo "==> Installing RixelPOS in $DEST"
mkdir -p "$DEST" "$(dirname "$BIN")" "$APPS" "$ICONS"
cp -a "$SOURCE/." "$DEST/"
ln -sf "$DEST/RixelPOS" "$BIN"
chmod +x "$DEST/RixelPOS" "$DEST/install.sh" 2>/dev/null || true

if [ -f "$SOURCE/rixelpos.desktop" ]; then
  cp "$SOURCE/rixelpos.desktop" "$APPS/rixelpos.desktop"
else
  cat > "$APPS/rixelpos.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=RixelPOS
Comment=Point of sale by RixelDev
Exec=$BIN
Icon=rixelpos
Terminal=false
Categories=Office;Finance;PointOfSale;
StartupWMClass=RixelPOS
EOF
fi

if [ -f "$SOURCE/rixelpos.png" ]; then
  cp "$SOURCE/rixelpos.png" "$ICONS/rixelpos.png"
fi

update-desktop-database "$APPS" 2>/dev/null || true

if [ "$WITH_DOTNET" -eq 1 ]; then
  SDK_SCRIPT="$SOURCE/prerequisites/install-dotnet.sh"
  [ -f "$SDK_SCRIPT" ] || SDK_SCRIPT="$SOURCE/../prerequisites/install-dotnet.sh"
  if [ -f "$SDK_SCRIPT" ]; then
    bash "$SDK_SCRIPT" 8.0
  else
    echo "!! SDK installer not bundled; download .NET from https://dotnet.microsoft.com/download"
  fi
fi

echo
echo "RixelPOS installed."
echo "  Application: $DEST/RixelPOS"
echo "  Launcher:    $BIN"
echo "  Data folder: ~/.local/share/rixelpos"
echo "  Open it from your application menu or run: rixelpos"
echo "  .NET SDK is optional; the app runs self-contained."
