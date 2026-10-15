#!/usr/bin/env bash
# Publishes the Linux build (Builds/Linux) as a zip on the GitHub Releases page.
# Run with --help for options.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/scripts/release-common.sh"

PLATFORM_LABEL="Linux"
UNITY_TARGET="StandaloneLinux64"
BUILD_SUBDIR="Linux"
BUILD_OUTPUT="WashedAshore.x86_64"
ASSET_SUFFIX="linux64"

find_player() {
    local bin
    bin="$(find "$1" -maxdepth 1 -type f -name '*.x86_64' 2>/dev/null | head -n 1)"
    [[ -n "$bin" ]] && echo "$bin"
}

# A zip made elsewhere can lose the executable bit; make sure players can run it.
post_stage() {
    find "$1" -maxdepth 1 -type f -name '*.x86_64' -exec chmod +x {} +
}

release_main "$@"
