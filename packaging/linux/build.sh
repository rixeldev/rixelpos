#!/usr/bin/env bash
# Builds the Linux distribution artifacts for RixelPOS.
# Usage: ./packaging/linux/build.sh [version]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${1:-1.0.0}"
RID="linux-x64"
OUT="$ROOT/dist/linux"
PUBLISH="$OUT/RixelPOS"
APPDIR="$OUT/AppDir"
PKG="$OUT/pkg"

echo "== RixelPOS $VERSION ($RID) =="
rm -rf "$OUT"
mkdir -p "$PUBLISH" "$APPDIR/usr/bin" "$APPDIR/usr/lib/rixelpos" \
  "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps" "$PKG/DEBIAN"

dotnet publish "$ROOT/src/RixelPOS.Desktop/RixelPOS.Desktop.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:DebugType=None -p:DebugSymbols=false \
  -o "$PUBLISH"

# Tar.gz portable
mkdir -p "$OUT/portable"
cp -a "$PUBLISH/." "$OUT/portable/"
cp "$ROOT/packaging/linux/rixelpos.desktop" "$OUT/portable/"
cp "$ROOT/src/RixelPOS.Desktop/Assets/rixelpos-256.png" "$OUT/portable/rixelpos.png"
tar -C "$OUT/portable" -czf "$OUT/rixelpos-${VERSION}-linux-x64.tar.gz" .

# Shared payload
cp -a "$PUBLISH/." "$APPDIR/usr/lib/rixelpos/"
cp "$ROOT/packaging/linux/rixelpos.desktop" "$APPDIR/usr/share/applications/rixelpos.desktop"
cp "$ROOT/src/RixelPOS.Desktop/Assets/rixelpos-256.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/rixelpos.png"

ln -sf /usr/lib/rixelpos/RixelPOS "$APPDIR/usr/bin/rixelpos"
cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/rixelpos/RixelPOS" "$@"
EOF
chmod +x "$APPDIR/AppRun"

# AppImage (optional tool)
if command -v appimagetool >/dev/null 2>&1; then
  echo "== AppImage =="
  ARCH=x86_64 appimagetool "$APPDIR" "$OUT/RixelPOS-${VERSION}-x86_64.AppImage" || true
else
  echo "!! appimagetool no está instalado; se omite el AppImage."
  echo "   Instala appimagetool y vuelve a ejecutar el script."
fi

# Debian package (optional tool)
if command -v dpkg-deb >/dev/null 2>&1; then
  echo "== .deb =="
  cp -a "$APPDIR/usr" "$PKG/"
  mkdir -p "$PKG/lib/udev/rules.d"
  cp "$ROOT/packaging/linux/udev/99-rixelpos.rules" "$PKG/lib/udev/rules.d/99-rixelpos.rules"
  cat > "$PKG/DEBIAN/control" <<EOF
Package: rixelpos
Version: ${VERSION}
Section: misc
Priority: optional
Architecture: amd64
Maintainer: RixelDev <soporte@rixeldev.com>
Description: RixelPOS - billing and point of sale by RixelDev
 Offline-first point of sale with ESC/POS receipts, inventory, cash register and reports.
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

# AUR-ready PKGBUILD
sed "s/@VERSION@/$VERSION/g" "$ROOT/packaging/linux/PKGBUILD" > "$OUT/PKGBUILD"
cp "$ROOT/packaging/linux/rixelpos.install" "$OUT/rixelpos.install"

echo
echo "Artefactos en $OUT:"
ls -lh "$OUT" | sed 's/^/  /'
echo
echo "Siguiente paso: instala el AppImage, el .deb o usa el PKGBUILD con makepkg -si."
