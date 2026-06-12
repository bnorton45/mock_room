#!/usr/bin/env bash
#
# Build a Debian (.deb) package for MockRoom from a NativeAOT linux-x64 publish.
#
# Usage:  packaging/linux/build-deb.sh [--no-publish]
#   --no-publish   reuse an existing publish output instead of rebuilding it
#
# Requires: dotnet (with the linux-x64 AOT toolchain — clang, zlib1g-dev),
#           dpkg-deb, fakeroot.  Output: packaging/dist/mockroom_<ver>_amd64.deb
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PKG_DIR="$REPO_ROOT/packaging"
ICONS_DIR="$PKG_DIR/icons"
DIST_DIR="$PKG_DIR/dist"
PROJECT="$REPO_ROOT/src/MockRoom/MockRoom.csproj"
RID="linux-x64"
PUBLISH_DIR="$REPO_ROOT/src/MockRoom/bin/Release/net10.0/$RID/publish"

# --- version ---------------------------------------------------------------
VERSION="$(grep -oPm1 '(?<=<Version>)[^<]+' "$PROJECT" || true)"
VERSION="${VERSION:-1.0.0}"
ARCH="amd64"
echo ">> MockRoom $VERSION ($ARCH)"

# --- icons -----------------------------------------------------------------
if [ ! -f "$ICONS_DIR/mockroom-256.png" ]; then
    echo ">> generating icons"
    python3 "$PKG_DIR/make-icons.py" "$ICONS_DIR"
fi

# --- publish ---------------------------------------------------------------
if [ "${1:-}" != "--no-publish" ]; then
    echo ">> dotnet publish (NativeAOT, $RID)"
    dotnet publish "$PROJECT" -r "$RID" -c Release -p:PublishAot=true
fi
if [ ! -x "$PUBLISH_DIR/MockRoom" ]; then
    echo "ERROR: publish output not found at $PUBLISH_DIR/MockRoom" >&2
    exit 1
fi

# --- stage tree ------------------------------------------------------------
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
echo ">> staging into $STAGE"

install -d "$STAGE/opt/mockroom" \
           "$STAGE/usr/bin" \
           "$STAGE/usr/share/applications" \
           "$STAGE/usr/share/metainfo" \
           "$STAGE/DEBIAN"

# App payload: AOT binary + the native libs it loads (Skia/HarfBuzz). Strip .pdb/.dbg.
install -m755 "$PUBLISH_DIR/MockRoom" "$STAGE/opt/mockroom/MockRoom"
for so in "$PUBLISH_DIR"/*.so; do
    [ -e "$so" ] && install -m644 "$so" "$STAGE/opt/mockroom/"
done

# Launcher on PATH — exec the real binary so its sibling .so files resolve.
cat > "$STAGE/usr/bin/mockroom" <<'EOF'
#!/bin/sh
exec /opt/mockroom/MockRoom "$@"
EOF
chmod 755 "$STAGE/usr/bin/mockroom"

# Desktop entry + icons (hicolor theme).
install -m644 "$PKG_DIR/linux/mockroom.desktop" "$STAGE/usr/share/applications/mockroom.desktop"
for size in 16 32 48 64 128 256 512; do
    dir="$STAGE/usr/share/icons/hicolor/${size}x${size}/apps"
    install -d "$dir"
    install -m644 "$ICONS_DIR/mockroom-${size}.png" "$dir/mockroom.png"
done

# --- control + maintainer scripts -----------------------------------------
INSTALLED_KB="$(du -sk "$STAGE/opt" "$STAGE/usr" | awk '{s+=$1} END {print s}')"
cat > "$STAGE/DEBIAN/control" <<EOF
Package: mockroom
Version: $VERSION
Section: graphics
Priority: optional
Architecture: $ARCH
Depends: libc6, libfontconfig1, libgl1, libx11-6, libice6, libsm6
Installed-Size: $INSTALLED_KB
Maintainer: MockRoom <packaging@mockroom.invalid>
Description: 3D room mock-up and floor-space planner
 MockRoom lets you enter room dimensions, place editable furniture and doors,
 and see how much usable floor space remains in a 2D top-down plan and an
 interactive 3D viewport. Self-contained NativeAOT build; no runtime required.
EOF

cat > "$STAGE/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi
EOF
chmod 755 "$STAGE/DEBIAN/postinst"
cp "$STAGE/DEBIAN/postinst" "$STAGE/DEBIAN/postrm"

# --- build -----------------------------------------------------------------
mkdir -p "$DIST_DIR"
OUT="$DIST_DIR/mockroom_${VERSION}_${ARCH}.deb"
fakeroot dpkg-deb --build --root-owner-group "$STAGE" "$OUT"
echo ">> built $OUT"
dpkg-deb --info "$OUT"
dpkg-deb --contents "$OUT"
