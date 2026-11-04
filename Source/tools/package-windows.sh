#!/usr/bin/env bash
# package-windows.sh - build the Windows 11 distribution of Buffy Chess.
#
# Produces, under dist/windows/:
#   BuffyChess/            the published, self-contained game folder
#   BuffyChess-1.0.0-win-x64.zip   the same folder, zipped for a portable install
#
# Usage:  tools/package-windows.sh [version]
#
# The publish is deliberately *self-contained*: a player must not have to install
# the .NET runtime to play chess. It is also single-file, so the folder does not
# greet them with 120 DLLs - MonoGame's native libs (SDL2, OpenAL) stay beside the
# executable, which is what keeps a single-file publish honest on DesktopGL.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$(cd "$HERE/.." && pwd)"
VERSION="${1:-1.0.0}"
OUT="$SRC/dist/windows"
APP="$OUT/BuffyChess"
PROJECT="$SRC/src/Chess.Desktop/Chess.Desktop.csproj"

echo "==> Publishing $PROJECT for win-x64 (version $VERSION)"
rm -rf "$APP"
mkdir -p "$OUT"

dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime win-x64 \
    --self-contained true \
    --output "$APP" \
    -p:Version="$VERSION" \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=false \
    -p:DebugType=none \
    -p:DebugSymbols=false

# A published Windows game should never ship its debugging symbols to players.
find "$APP" -name '*.pdb' -delete

echo "==> Zipping the portable build"
ZIP="$OUT/BuffyChess-$VERSION-win-x64.zip"
rm -f "$ZIP"
( cd "$APP" && zip -qr "$ZIP" . )
echo "    $ZIP  ($(du -h "$ZIP" | cut -f1))"

# Signing is a separate, deliberate step: it needs a certificate and a timestamp
# server, both of which belong to the publisher, not to this repository.
if [[ -n "${SIGN_CERT:-}" ]]; then
    echo "==> Signing with $SIGN_CERT"
    signtool sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com \
        /f "$SIGN_CERT" /p "${SIGN_PASS:-}" "$APP/Chess.Desktop.exe"
else
    echo "==> Skipping Authenticode signing (set SIGN_CERT to enable)"
    echo "    Unsigned builds trigger SmartScreen's 'unrecognised app' prompt."
fi

echo "==> Done. Executable: $APP/Chess.Desktop.exe"
