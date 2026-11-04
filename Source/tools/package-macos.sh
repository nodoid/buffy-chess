#!/usr/bin/env bash
# package-macos.sh - build Buffy Chess.app for macOS 26+ (Apple silicon and Intel).
#
# The macOS target is the DesktopGL shell, not Mac Catalyst: MonoGame's iOS
# framework ships only net8.0-ios assets, so a Catalyst build has no MonoGame to
# link against. DesktopGL is the supported route on the Mac, and it is the same
# shared shell the Windows build uses - so the game is identical, and only the
# bundle wrapper differs.
#
# Produces: dist/macos/BuffyChess.app
#
# Usage: tools/package-macos.sh [version] [rid]
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$(cd "$HERE/.." && pwd)"
VERSION="${1:-1.0.0}"
RID="${2:-$(uname -m | sed 's/^arm64$/osx-arm64/; s/^x86_64$/osx-x64/')}"
OUT="$SRC/dist/macos"
APP="$OUT/BuffyChess.app"
PROJECT="$SRC/src/Chess.Desktop/Chess.Desktop.csproj"

echo "==> Publishing $PROJECT for $RID"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    --output "$APP/Contents/MacOS" \
    -p:Version="$VERSION" \
    -p:DebugType=none \
    -p:DebugSymbols=false

find "$APP" -name '*.pdb' -delete

# --- icon: PNG master -> .iconset -> .icns --------------------------------
ICONSET="$OUT/AppIcon.iconset"
rm -rf "$ICONSET"; mkdir -p "$ICONSET"
MASTER="$SRC/Assets/Branding/icon-1024.png"
for s in 16 32 64 128 256 512; do
    sips -z $s $s "$MASTER" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
    d=$((s * 2))
    sips -z $d $d "$MASTER" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$ICONSET"

# --- Info.plist: the minimum a launchable .app needs ----------------------
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>Buffy Chess</string>
    <key>CFBundleDisplayName</key><string>Buffy Chess</string>
    <key>CFBundleIdentifier</key><string>uk.co.chessbook.BuffyChess</string>
    <key>CFBundleExecutable</key><string>Chess.Desktop</string>
    <key>CFBundleIconFile</key><string>AppIcon</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundleVersion</key><string>$VERSION</string>
    <key>LSMinimumSystemVersion</key><string>26.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST

# Ad-hoc signature: enough to launch locally and to satisfy Gatekeeper on the
# machine that built it. A distributed build needs a Developer ID identity.
#
# The build tree is inside an iCloud-managed Documents folder, so its FileProvider
# daemon re-tags freshly written files with com.apple.FinderInfo and fpfs
# metadata that codesign rejects as "resource fork ... detritus". Clearing and
# signing in one loop closes that window, exactly as the iOS build does.
for attempt in 1 2 3 4 5; do
    xattr -cr "$APP"
    if codesign --force --deep --sign - "$APP" 2>/dev/null; then break; fi
    sleep 1
done
codesign --verify --verbose=1 "$APP" 2>&1 | sed 's/^/    /'

echo "==> Built $APP"
du -sh "$APP" | sed 's/^/    /'
