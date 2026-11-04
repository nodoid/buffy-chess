#!/usr/bin/env bash
# package-ios.sh - build Chess.iOS.app for iOS 18+ hardware, or the simulator.
#
#     tools/package-ios.sh 1.0.0                       # simulator build
#     tools/package-ios.sh 1.0.0 ios-arm64 KEY PROFILE # signed device build
#
# Why the SDK's own signing is suppressed here: the build tree lives in an
# iCloud-managed folder and the FileProvider daemon re-tags fresh files faster
# than the SDK's Codesign MSBuild task can run, defeating it with
# "resource fork, Finder information, or similar detritus". The SDK still
# stages the bundle correctly - embedded.mobileprovision and
# archived-expanded-entitlements.xcent are both in place - so the one thing
# left is a single shell line: clear the attributes and sign in the same
# breath, the same trick package-macos.sh uses at the end.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$(cd "$HERE/.." && pwd)"
VERSION="$1"
RID="${2:-iossimulator-arm64}"
OUT="$SRC/dist/ios"
APP="$OUT/Chess.iOS.app"
PROJECT="$SRC/platforms/iOS/Chess.iOS.csproj"

COMMON=(-p:ValidateXcodeVersion=false
        -p:EnableCodeSigning=false
        -p:RuntimeIdentifier="$RID")

echo "==> Building $PROJECT for $RID"
dotnet build "$PROJECT" -c Release -r "$RID" -f net10.0-ios \
    "${COMMON[@]}"

from_build="$SRC/platforms/iOS/bin/Release/net10.0-ios/$RID/Chess.iOS.app"
rm -rf "$APP"
mkdir -p "$OUT"
cp -R "$from_build" "$APP"

if [[ "$RID" == iossimulator* ]]; then
    # The simulator takes ad-hoc signatures only; the retry loop is the same
    # one BrandAndSignBundle uses inside the csproj.
    for attempt in 1 2 3 4 5; do
        xattr -cr "$APP"
        if codesign --force --sign - "$APP" 2>/dev/null; then break; fi
        sleep 1
    done
else
    # Device build: a real development identity and the profile that covers
    # the App ID. Pass both the key and the profile (by name or UUID).
    KEY="${3:?device build needs: package-ios.sh <version> ios-arm64 <key> <profile>}"
    PROFILE="${4:?device build needs: package-ios.sh <version> ios-arm64 <key> <profile>}"
    echo "==> Codesigning with '$KEY' / '$PROFILE'"
    for attempt in 1 2 3 4 5; do
        xattr -cr "$APP"
        if codesign --force --sign "$KEY" \
                --entitlements "$APP/archived-expanded-entitlements.xcent" \
                "$APP" 2>/dev/null; then break; fi
        sleep 1
    done
    codesign --verify --verbose=1 "$APP" 2>&1 | sed 's/^/    /'
fi

codesign --verify "$APP" && echo "==> Built $APP"
du -sh "$APP" | sed 's/^/    /'
