# Shared by release-windows.sh, release-mac.sh and release-linux.sh. Source it, don't run it.
#
# The caller sets these, then calls release_main "$@":
#   PLATFORM_LABEL   shown to the user, e.g. "Windows"
#   UNITY_TARGET     unity CLI --target value, e.g. StandaloneWindows64
#   BUILD_SUBDIR     folder under Builds/, e.g. Windows
#   BUILD_OUTPUT     output path relative to Builds/<BUILD_SUBDIR>
#   ASSET_SUFFIX     zip name suffix, e.g. win64
#   find_player      shell function: given the build dir, prints the player and returns 0 if found
#   post_stage       optional shell function: given the staged folder, fixes it up before zipping

REPO="chz160/washed-ashore"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ -t 1 ]]; then CYAN=$'\033[36m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'; RESET=$'\033[0m'; else CYAN=; GREEN=; YELLOW=; RESET=; fi
info() { printf '%s%s%s\n' "$CYAN" "$*" "$RESET"; }
warn() { printf '%sWARNING: %s%s\n' "$YELLOW" "$*" "$RESET" >&2; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

release_usage() {
    cat <<EOF
Usage: $(basename "$0") [--build] [--notes TEXT] [--prerelease] [--draft] [-h|--help]

Publishes the $PLATFORM_LABEL build (Builds/$BUILD_SUBDIR) as a zip on the GitHub Releases page.

Copies Builds/$BUILD_SUBDIR to a staging folder (leaving out the folders Unity marks
"DoNotShip", build provenance and log files), zips it, and creates a GitHub release
with the zip attached, using the gh CLI.

The version is the current time as yyyy.MM.dd.HHmm, for example 2026.10.06.1430, and
the tag is that version with a "v" in front. The tag points at the current commit,
which must already be pushed.

  --build         Rebuild the $PLATFORM_LABEL player with the unity CLI before releasing.
                  The Unity editor needs the $PLATFORM_LABEL Build Support module installed.
  --notes TEXT    Release notes. Defaults to the commit the release is tagged on.
  --prerelease    Mark the release as a pre-release.
  --draft         Create the release as a draft, so it is not public until you publish it on GitHub.
  -h, --help      Show this help.
EOF
}

release_main() {
    local build=0 notes="" prerelease=0 draft=0
    while (($#)); do
        case "$1" in
            --build) build=1 ;;
            --notes) (($# >= 2)) || die "--notes needs a value."; notes="$2"; shift ;;
            --notes=*) notes="${1#--notes=}" ;;
            --prerelease) prerelease=1 ;;
            --draft) draft=1 ;;
            -h | --help) release_usage; exit 0 ;;
            *) release_usage >&2; die "Unknown option: $1" ;;
        esac
        shift
    done

    local build_dir="$ROOT/Builds/$BUILD_SUBDIR"
    local version tag
    version="$(date +%Y.%m.%d.%H%M)"
    tag="v$version"

    command -v gh >/dev/null || die "gh not found. Install the GitHub CLI from https://cli.github.com, then run 'gh auth login'."
    command -v zip >/dev/null || die "zip not found. Install it with your package manager (for example 'sudo apt install zip')."
    gh auth status >/dev/null 2>&1 || die "gh is not logged in. Run 'gh auth login' first."

    # The tag is created on GitHub, so the commit has to be there already.
    local commit
    commit="$(git -C "$ROOT" rev-parse HEAD)"
    [[ -n "$(git -C "$ROOT" branch -r --contains "$commit")" ]] || die "Commit ${commit:0:7} is not on GitHub yet. Push it first."
    if [[ -n "$(git -C "$ROOT" status --porcelain --untracked-files=no)" ]]; then
        warn "The working tree has uncommitted changes. The release is tagged on ${commit:0:7}, which may not match the build."
    fi

    if ((build)); then
        command -v unity >/dev/null || die "unity CLI not found. Install it and sign in, then try again."
        info "Building the $PLATFORM_LABEL player (this takes a few minutes)..."
        mkdir -p "$ROOT/Builds"
        unity build "$ROOT" \
            --target "$UNITY_TARGET" \
            --output-path "$build_dir/$BUILD_OUTPUT" \
            --log-file "$ROOT/Builds/build-$BUILD_SUBDIR-release.log" \
            --no-tail --timeout 3600 ||
            die "Unity build failed. See Builds/build-$BUILD_SUBDIR-release.log."
    fi

    find_player "$build_dir" >/dev/null || die "No $PLATFORM_LABEL build at $build_dir. Build it first, or run with --build."

    # Stage a clean copy inside a "WashedAshore" folder, so the zip extracts into one folder.
    local stage zip
    STAGE_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/washed-ashore-release.XXXXXX")"
    trap 'rm -rf "$STAGE_ROOT"' EXIT
    local stage_root="$STAGE_ROOT"
    stage="$stage_root/WashedAshore"
    cp -a "$build_dir" "$stage"
    find "$stage" -type d \( -name '*DoNotShip*' -o -name '*DontShip*' \) -prune -exec rm -rf {} +
    find "$stage" -type f \( -name '*.provenance.json' -o -name '*.log' \) -delete
    if declare -F post_stage >/dev/null; then post_stage "$stage"; fi

    zip="$stage_root/WashedAshore-$version-$ASSET_SUFFIX.zip"
    info "Zipping $version..."
    # -y keeps symlinks as symlinks (needed inside a macOS .app); zip also keeps the executable bit.
    (cd "$stage_root" && zip -r -y -q "$zip" WashedAshore)

    # GitHub rejects release assets of 2 GiB or more.
    local zip_bytes zip_mb built
    zip_bytes="$(stat -c %s "$zip")"
    ((zip_bytes < 2147483648)) || die "The zip is $((zip_bytes / 1048576)) MB, over GitHub's 2 GiB limit for a release file."
    zip_mb="$(awk -v b="$zip_bytes" 'BEGIN { printf "%.1f", b / 1048576 }')"
    built="$(find "$stage" -type f -printf '%TY-%Tm-%Td %TH:%TM\n' | sort | tail -n 1)"
    info "Releasing $tag from the build of $built ($zip_mb MB zipped)"

    [[ -n "$notes" ]] || notes="$PLATFORM_LABEL build of $(git -C "$ROOT" log -1 --format='%h %s' "$commit")"
    local args=(release create "$tag" "$zip" --repo "$REPO" --target "$commit" --title "Washed Ashore $version" --notes "$notes")
    ((prerelease)) && args+=(--prerelease)
    ((draft)) && args+=(--draft)
    local url
    url="$(gh "${args[@]}")" || die "gh release create failed."

    echo
    printf '%sReleased at %s%s\n' "$GREEN" "$url" "$RESET"
}
