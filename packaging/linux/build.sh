#!/usr/bin/env bash
# Builds the Linux distribution artifacts for RixelPOS.
# Usage: ./packaging/linux/build.sh [version]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${1:-1.0.0}"
PKGVER="${VERSION//-/.}"
PKGREL="1"
NAME="rixelpos"
RID="linux-x64"
ARCH="x86_64"
OUT="$ROOT/dist/linux"
PUBLISH="$OUT/RixelPOS"
APPDIR="$OUT/AppDir"
PKG="$OUT/pkg"
ARCHPKG="$OUT/archpkg"
PACMAN_REPO="$OUT/pacman/$ARCH"

echo "== RixelPOS $VERSION ($RID) =="
rm -rf "$OUT"
mkdir -p "$PUBLISH" "$APPDIR/usr/bin" "$APPDIR/usr/lib/rixelpos" \
  "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps" \
  "$PKG/DEBIAN"

dotnet publish "$ROOT/src/RixelPOS.Desktop/RixelPOS.Desktop.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:Version="$VERSION" -p:DebugType=None -p:DebugSymbols=false \
  -o "$PUBLISH"

# Optional .NET SDK bootstrap and portable installer
mkdir -p "$PUBLISH/prerequisites"
cp "$ROOT/packaging/prerequisites/install-dotnet.sh" "$PUBLISH/prerequisites/"
cp "$ROOT/packaging/prerequisites/install-dotnet.ps1" "$PUBLISH/prerequisites/"
cp "$ROOT/packaging/linux/install.sh" "$PUBLISH/install.sh"
chmod +x "$PUBLISH/install.sh" "$PUBLISH/prerequisites/install-dotnet.sh"
cp "$ROOT/packaging/linux/rixelpos.desktop" "$PUBLISH/"
cp "$ROOT/src/RixelPOS.Desktop/Assets/rixelpos-256.png" "$PUBLISH/rixelpos.png"

# Tar.gz portable (includes install.sh, desktop entry, icon and SDK bootstrap)
tar -C "$PUBLISH" -czf "$OUT/rixelpos-${VERSION}-linux-x64.tar.gz" .

# Shared payload
cp -a "$PUBLISH/." "$APPDIR/usr/lib/rixelpos/"
cp "$ROOT/packaging/linux/rixelpos.desktop" "$APPDIR/usr/share/applications/rixelpos.desktop"
cp "$ROOT/src/RixelPOS.Desktop/Assets/rixelpos-256.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/rixelpos.png"

ln -sf /usr/lib/rixelpos/RixelPOS "$APPDIR/usr/bin/rixelpos"
cat > "$APPDIR/usr/bin/rixelpos-install-dotnet" <<'EOF'
#!/bin/sh
exec /usr/lib/rixelpos/prerequisites/install-dotnet.sh "$@"
EOF
chmod +x "$APPDIR/usr/bin/rixelpos-install-dotnet"
cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/rixelpos/RixelPOS" "$@"
EOF
chmod +x "$APPDIR/AppRun"

# AppImage (optional tool; set APPIMAGETOOL or install appimagetool)
APPIMAGETOOL_BIN="${APPIMAGETOOL:-$(command -v appimagetool || true)}"
if [ -n "$APPIMAGETOOL_BIN" ] && [ -x "$APPIMAGETOOL_BIN" ]; then
  echo "== AppImage =="
  APPIMAGE_EXTRACT_AND_RUN=1 ARCH=x86_64 "$APPIMAGETOOL_BIN" "$APPDIR" \
    "$OUT/RixelPOS-${VERSION}-x86_64.AppImage" || true
else
  echo "!! appimagetool no está instalado; se omite el AppImage."
  echo "   Define APPIMAGETOOL=/ruta/appimagetool o instala appimagetool."
fi

# Debian package (optional tool)
if command -v dpkg-deb >/dev/null 2>&1; then
  echo "== .deb =="
  cp -a "$APPDIR/usr" "$PKG/"
  mkdir -p "$PKG/usr/lib/udev/rules.d" "$PKG/usr/share/licenses/rixelpos"
  cp "$ROOT/packaging/linux/udev/99-rixelpos.rules" "$PKG/usr/lib/udev/rules.d/99-rixelpos.rules"
  cp "$ROOT/LICENSE" "$PKG/usr/share/licenses/rixelpos/LICENSE"
  cat > "$PKG/DEBIAN/control" <<EOF
Package: rixelpos
Version: ${VERSION}
Section: misc
Priority: optional
Architecture: amd64
Maintainer: RixelDev <support@rixel.dev>
Recommends: dotnet-sdk-8.0 | dotnet-sdk-10.0
Description: RixelPOS - billing and point of sale by RixelDev
 Offline-first point of sale with ESC/POS receipts, inventory, cash register and reports.
 The application is self-contained; the .NET SDK is optional for development.
EOF
  cat > "$PKG/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
udevadm control --reload-rules >/dev/null 2>&1 || true
exit 0
EOF
  chmod +x "$PKG/DEBIAN/postinst"
  dpkg-deb --build --root-owner-group "$PKG" "$OUT/rixelpos_${VERSION}_amd64.deb"
else
  echo "!! dpkg-deb no está instalado; se omite el .deb."
fi

# Arch Linux / CachyOS package
echo "== Arch package =="
mkdir -p "$ARCHPKG" "$ARCHPKG/usr/share/licenses/rixelpos" "$ARCHPKG/usr/lib/udev/rules.d"
cp -a "$APPDIR/usr" "$ARCHPKG/"
cp "$ROOT/LICENSE" "$ARCHPKG/usr/share/licenses/rixelpos/LICENSE"
cp "$ROOT/packaging/linux/udev/99-rixelpos.rules" "$ARCHPKG/usr/lib/udev/rules.d/99-rixelpos.rules"
cp "$ROOT/packaging/linux/rixelpos.install" "$ARCHPKG/.INSTALL"
BUILD_DATE="$(date +%s)"
INSTALLED_SIZE="$(du -sb "$ARCHPKG/usr" | cut -f1)"
cat > "$ARCHPKG/.PKGINFO" <<EOF
pkgname = $NAME
pkgbase = $NAME
pkgver = $PKGVER-$PKGREL
pkgdesc = Offline-first billing and point of sale by RixelDev
url = https://rixel.dev
builddate = $BUILD_DATE
packager = RixelDev <support@rixel.dev>
size = $INSTALLED_SIZE
arch = $ARCH
license = custom
depend = glibc
depend = icu
depend = fontconfig
depend = freetype2
depend = harfbuzz
depend = zlib
depend = libx11
depend = libice
depend = libsm
depend = libxext
depend = libxrandr
depend = libxi
depend = libxcursor
depend = libxrender
depend = libxcb
depend = mesa
depend = libglvnd
optdepend = dotnet-sdk: optional SDK for development and source builds
EOF
ARCH_FILE="$OUT/${NAME}-${PKGVER}-${PKGREL}-${ARCH}.pkg.tar.zst"
( cd "$ARCHPKG" && bsdtar --zstd -cf "$ARCH_FILE" .PKGINFO .INSTALL usr )
echo "  $ARCH_FILE"

# Pacman repository (for the optional custom repo / GitHub Pages)
mkdir -p "$PACMAN_REPO"
python3 "$ROOT/packaging/linux/make-repo.py" \
  --packages "$OUT" --output "$PACMAN_REPO" --repo "$NAME" --arch "$ARCH" \
  --server "https://rixeldev.github.io/rixelpos/pacman/\$arch"

# AUR-ready PKGBUILD + helper scripts
sed "s/@VERSION@/$PKGVER/g" "$ROOT/packaging/linux/PKGBUILD" > "$OUT/PKGBUILD"
cp "$ROOT/packaging/linux/rixelpos.install" "$OUT/rixelpos.install"
cp "$ROOT/packaging/linux/install.sh" "$OUT/install.sh"
chmod +x "$OUT/install.sh" "$OUT/rixelpos.install" 2>/dev/null || true

echo
echo "Artefactos en $OUT:"
ls -lh "$OUT" | sed 's/^/  /'
echo
echo "Instaladores:"
echo "  Linux portable:  tar -xzf rixelpos-${VERSION}-linux-x64.tar.gz && cd rixelpos-* && ./install.sh"
echo "  Arch directo:    sudo pacman -U rixelpos-${PKGVER}-${PKGREL}-${ARCH}.pkg.tar.zst"
echo "  Repositorio:     agrega dist/linux/pacman/x86_64/pacman.conf a /etc/pacman.conf y 'sudo pacman -Syu rixelpos'"
