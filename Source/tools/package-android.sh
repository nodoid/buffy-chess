#!/usr/bin/env bash
# package-android.sh - build the Android distribution of Buffy Chess.
#
# Produces, under dist/android/:
#   BuffyChess-<version>.apk   installable on Android 12L (API 32) and newer
#   BuffyChess-<version>.aab   the Play Store upload bundle
#
# Usage:
#   tools/package-android.sh [version]
#   KEYSTORE=release.jks KEY_ALIAS=buffy KEY_PASS=... STORE_PASS=... \
#       tools/package-android.sh 1.0.0
#
# With no keystore the build is signed with the SDK's throwaway debug key, which
# is fine for sideloading and useless for the Play Store: Google rejects a bundle
# signed with a debug key, and a debug-signed APK cannot be updated by a
# store-signed one.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$(cd "$HERE/.." && pwd)"
VERSION="${1:-1.0.0}"
# versionCode must be a monotonically increasing integer; derive it from the
# semver so 1.0.0 -> 10000 and 1.2.3 -> 10203 without anyone maintaining it.
VERSION_CODE="${VERSION_CODE:-$(echo "$VERSION" | awk -F. '{printf "%d%02d%02d", $1, $2, $3}')}"
OUT="$SRC/dist/android"
PROJECT="$SRC/platforms/Android/Chess.Android.csproj"

mkdir -p "$OUT"

SIGN_ARGS=()
if [[ -n "${KEYSTORE:-}" ]]; then
    SIGN_ARGS=(
        -p:AndroidKeyStore=true
        -p:AndroidSigningKeyStore="$KEYSTORE"
        -p:AndroidSigningKeyAlias="${KEY_ALIAS:-buffy}"
        -p:AndroidSigningKeyPass="$KEY_PASS"
        -p:AndroidSigningStorePass="$STORE_PASS"
    )
    echo "==> Signing with $KEYSTORE (alias ${KEY_ALIAS:-buffy})"
else
    echo "==> No KEYSTORE set: the SDK's debug key will be used"
fi

echo "==> Publishing APK (version $VERSION)"
dotnet publish "$PROJECT" \
    --configuration Release \
    --framework net10.0-android \
    --output "$OUT/publish-apk" \
    -p:ApplicationDisplayVersion="$VERSION" \
    -p:ApplicationVersion="$VERSION_CODE" \
    -p:AndroidPackageFormat=apk \
    -p:EmbedAssembliesIntoApk=true \
    -p:AndroidLinkMode=SdkOnly \
    ${SIGN_ARGS[@]+"${SIGN_ARGS[@]}"}

echo "==> Publishing AAB (Play Store bundle)"
dotnet publish "$PROJECT" \
    --configuration Release \
    --framework net10.0-android \
    --output "$OUT/publish-aab" \
    -p:ApplicationDisplayVersion="$VERSION" \
    -p:ApplicationVersion="$VERSION_CODE" \
    -p:AndroidPackageFormat=aab \
    -p:AndroidLinkMode=SdkOnly \
    ${SIGN_ARGS[@]+"${SIGN_ARGS[@]}"}

# Collect the artefacts and give them names a human can read.
find "$OUT/publish-apk" -name '*-Signed.apk' -exec cp {} "$OUT/BuffyChess-$VERSION.apk" \;
find "$OUT/publish-aab" -name '*.aab' -exec cp {} "$OUT/BuffyChess-$VERSION.aab" \;

for f in "$OUT/BuffyChess-$VERSION.apk" "$OUT/BuffyChess-$VERSION.aab"; do
    [[ -f "$f" ]] && echo "    $f  ($(du -h "$f" | cut -f1))"
done

# The manifest is the contract with the device: minimum API, target API, label
# and launcher icon. Dump it rather than trusting the csproj.
AAPT2="$(ls "${ANDROID_HOME:-$HOME/Library/Android/sdk}"/build-tools/*/aapt2 2>/dev/null | tail -1)"
if [[ -n "$AAPT2" && -f "$OUT/BuffyChess-$VERSION.apk" ]]; then
    echo "==> Manifest of the shipped APK"
    "$AAPT2" dump badging "$OUT/BuffyChess-$VERSION.apk" \
        | grep -E "^(package|sdkVersion|targetSdkVersion|application-label|application-icon-640|launchable-activity)" \
        | sed 's/^/    /'
fi

echo "==> Done."
