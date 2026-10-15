#!/usr/bin/env bash
# Publishes the macOS build (Builds/Mac/WashedAshore.app) as a zip on the GitHub Releases page.
# Run with --help for options.
#
# The app is not code-signed or notarized, so players have to right-click > Open the first time
# (or run: xattr -dr com.apple.quarantine WashedAshore.app).
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/scripts/release-common.sh"

PLATFORM_LABEL="macOS"
UNITY_TARGET="StandaloneOSX"
BUILD_SUBDIR="Mac"
BUILD_OUTPUT="WashedAshore.app"
ASSET_SUFFIX="mac"

find_player() {
    local app
    app="$(find "$1" -maxdepth 1 -type d -name '*.app' 2>/dev/null | head -n 1)"
    [[ -n "$app" ]] && echo "$app"
}

# Unity's macOS player binary must stay executable inside the bundle.
post_stage() {
    find "$1" -path '*/Contents/MacOS/*' -type f -exec chmod +x {} +
}

release_main "$@"
