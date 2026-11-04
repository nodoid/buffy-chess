#!/usr/bin/env bash
# build-all.sh - build and package every target from one command.
##    tools/build-all.sh                # everything that needs no secrets
#     CODESIGN_KEY=... CODESIGN_PROVISION="..." \
#     tools/build-all.sh 1.2.3          # same, plus a signed iPhone build
#
# Stages, in order (each prints ==> and each failure stops the run):
#   1. dotnet test                     the one suite all targets share
#   2. Windows   dist/windows/*        portable zip, .ico labelled exe
#   3. macOS     dist/macos/*.app      bundle, ad-hoc signed
#   4. Android   dist/android/*        release APK + AAB (debug-signed
#                                      unless KEYSTORE is exported)
#   5. iOS       dist/ios/*            simulator .app; device build only if
#                                      CODESIGN_PROVISION is exported
#
# Usage (from a cron, a CI job or a habit): bash tools/build-all.sh [version]
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$(cd "$HERE/.." && pwd)"
VERSION="${1:-1.0.0}"
cd "$SRC"
STATUS=0

step() {
    echo
    echo "==> $1"
}

run() {
    # run <label> <cmd...>: capture the artefact list on success
    local label="$1"; shift
    step "$label"
    if "$@"; then
        echo "    ok: $label"
    else
        echo "    FAILED: $label (exit $?)" >&2
        STATUS=1
        return 1
    fi
}

# ---- 1. tests ----------------------------------------------------------------
step "1/5  dotnet test"
if dotnet test tests/Chess.Core.Tests/Chess.Core.Tests.csproj -c Release --nologo; then
    echo "    ok: tests"
else
    echo "    FAILED: tests" >&2
    exit 1
fi

# ---- 2. Windows --------------------------------------------------------------
run "2/5  windows" bash tools/package-windows.sh "$VERSION" || STATUS=1

# ---- 3. macOS ----------------------------------------------------------------
run "3/5  macos" bash tools/package-macos.sh "$VERSION" || STATUS=1

# ---- 4. Android --------------------------------------------------------------
# KEYSTORE / KEY_ALIAS / KEY_PASS exported here make a Play-ready signed build.
run "4/5  android" bash tools/package-android.sh "$VERSION" || STATUS=1

# ---- 5. iOS ------------------------------------------------------------------
step "5/5  ios"
if [ "${CODESIGN_PROVISION:-}" != "" ]; then
    # Signed device build: an identity and a profile covering the App ID.
    run "5/5  ios (device)" bash tools/package-ios.sh "$VERSION" ios-arm64 \
        "${CODESIGN_KEY:-Apple Development: Paul Johnson (WFWPL93CN6)}" \
        "$CODESIGN_PROVISION" || STATUS=1
else
    # Simulator build: ad hoc signed, installable on xcrun simctl devices only.
    run "5/5  ios (simulator)" bash tools/package-ios.sh "$VERSION" || STATUS=1
    echo "    note: simulator build (export CODESIGN_PROVISION and CODESIGN_KEY for a device build)"
fi

# ---- summary -----------------------------------------------------------------
step "artefacts"
if [ -d dist ]; then
    du -sh dist/* 2>/dev/null | sed 's/^/    /'
fi
if [ "$STATUS" = "0" ]; then
    echo
    echo "==> build-all: OK"
else
    echo
    echo "==> build-all: FAILED (see steps above)" >&2
fi
exit "$STATUS"
