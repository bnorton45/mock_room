#!/usr/bin/env bash
#
# Build a portable AppImage for MockRoom from a NativeAOT linux-x64 publish.
#
# Usage:  packaging/linux/build-appimage.sh [--no-publish]
#
# Requires: dotnet (linux-x64 AOT toolchain).  appimagetool is fetched into
# packaging/.tools/ automatically if it is not already on PATH. Runs the tool
# with --appimage-extract-and-run so no FUSE/libfuse2 is required.
# Output: packaging/dist/MockRoom-<ver>-x86_64.AppImage
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PKG_DIR="$REPO_ROOT/packaging"
ICONS_DIR="$PKG_DIR/icons"
DIST_DIR="$PKG_DIR/dist"
TOOLS_DIR="$PKG_DIR/.tools"
PROJECT="$REPO_ROOT/src/MockRoom/MockRoom.csproj"
RID="linux-x64"
PUBLISH_DIR="$REPO_ROOT/src/MockRoom/bin/Release/net10.0/$RID/publish"

VERSION="$(grep -oPm1 '(?<=<Version>)[^<]+' "$PROJECT" || true)"
VERSION="${VERSION:-1.0.0}"
echo ">> MockRoom $VERSION AppImage (x86_64)"

if [ ! -f "$ICONS_DIR/mockroom-256.png" ]; then
    python3 "$PKG_DIR/make-icons.py" "$ICONS_DIR"
fi

if [ "${1:-}" != "--no-publish" ]; then
    dotnet publish "$PROJECT" -r "$RID" -c Release -p:PublishAot=true
fi
if [ ! -x "$PUBLISH_DIR/MockRoom" ]; then
    echo "ERROR: publish output not found at $PUBLISH_DIR/MockRoom" >&2
    exit 1
fi

# --- appimagetool ----------------------------------------------------------
APPIMAGETOOL="$(command -v appimagetool || true)"
if [ -z "$APPIMAGETOOL" ]; then
    mkdir -p "$TOOLS_DIR"
    APPIMAGETOOL="$TOOLS_DIR/appimagetool-x86_64.AppImage"
    if [ ! -x "$APPIMAGETOOL" ]; then
        echo ">> fetching appimagetool"
        curl -fL -o "$APPIMAGETOOL" \
            https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
        chmod +x "$APPIMAGETOOL"
    fi
fi

# --- AppDir ----------------------------------------------------------------
APPDIR="$(mktemp -d)/MockRoom.AppDir"
trap 'rm -rf "$(dirname "$APPDIR")"' EXIT
install -d "$APPDIR/usr/bin" "$APPDIR/usr/share/applications"

install -m755 "$PUBLISH_DIR/MockRoom" "$APPDIR/usr/bin/MockRoom"
for so in "$PUBLISH_DIR"/*.so; do
    [ -e "$so" ] && install -m644 "$so" "$APPDIR/usr/bin/"
done

# .desktop (AppImage requires one at the AppDir root and under applications/).
install -m644 "$PKG_DIR/linux/mockroom.desktop" "$APPDIR/usr/share/applications/mockroom.desktop"
cp "$APPDIR/usr/share/applications/mockroom.desktop" "$APPDIR/mockroom.desktop"

# Icons: top-level .DirIcon + hicolor theme + the .desktop's Icon= name.
cp "$ICONS_DIR/mockroom-256.png" "$APPDIR/mockroom.png"
cp "$ICONS_DIR/mockroom-256.png" "$APPDIR/.DirIcon"
for size in 16 32 48 64 128 256 512; do
    d="$APPDIR/usr/share/icons/hicolor/${size}x${size}/apps"
    install -d "$d"
    install -m644 "$ICONS_DIR/mockroom-${size}.png" "$d/mockroom.png"
done

# AppRun — exec the binary so its sibling .so files resolve.
cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/MockRoom" "$@"
EOF
chmod 755 "$APPDIR/AppRun"

# --- build -----------------------------------------------------------------
mkdir -p "$DIST_DIR"
OUT="$DIST_DIR/MockRoom-${VERSION}-x86_64.AppImage"
ARCH=x86_64 "$APPIMAGETOOL" --appimage-extract-and-run "$APPDIR" "$OUT"
echo ">> built $OUT"
