#!/usr/bin/env bash
# Publishes the Windows build (Builds/Windows) as a zip on the GitHub Releases page.
# Run with --help for options. Linux counterpart of release-windows.ps1.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/scripts/release-common.sh"

PLATFORM_LABEL="Windows"
UNITY_TARGET="StandaloneWindows64"
BUILD_SUBDIR="Windows"
BUILD_OUTPUT="WashedAshorePOC.exe"
ASSET_SUFFIX="win64"

find_player() {
    local exe
    exe="$(find "$1" -maxdepth 1 -type f -name '*.exe' ! -name 'UnityCrashHandler64.exe' 2>/dev/null | head -n 1)"
    [[ -n "$exe" ]] && echo "$exe"
}

release_main "$@"
