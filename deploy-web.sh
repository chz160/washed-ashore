#!/usr/bin/env bash
# Deploys the Unity web build (Builds/Web) to Cloudflare Pages. Linux counterpart of deploy-web.ps1.
#
# Copies Builds/Web to a staging folder (leaving out build provenance files), checks it against
# the Pages limits, and uploads it with Wrangler. The first run opens a browser so you can log in
# to Cloudflare (or set CLOUDFLARE_API_TOKEN to skip that), and creates the Pages project if it
# does not exist yet.
#
# The build uses Brotli with Unity's decompression fallback, so the loader decompresses in
# JavaScript and Pages needs no special headers.
#
# Pages rejects any file over 25 MiB. Larger files (in practice Web.data) go to an R2 bucket
# instead, under a key made from the file's hash, and index.html is rewritten to load them from
# there. R2 must be enabled on the Cloudflare account once, in the dashboard. The long-term fix is
# splitting content with Addressables; see
# _bmad-output/planning-artifacts/deferred-addressables-content-streaming.md.
#
# Requires: Node.js 18+ (for npx), find, sha256sum.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WEB_DIR="$ROOT/Builds/Web"

PROJECT_NAME="washed-ashore-game"
BUILD=0
BRANCH="main"
MESSAGE="Web build $(date '+%Y-%m-%d %H:%M')"
BUCKET="washed-ashore-game-data"
DATA_BASE_URL=""

PAGES_LIMIT=26214400        # 25 MiB
R2_UPLOAD_LIMIT=314572800   # 300 MB, the largest file Wrangler can upload to R2
DATA_WARN_LIMIT=104857600   # 100 MB

if [[ -t 1 ]]; then CYAN=$'\033[36m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'; RESET=$'\033[0m'; else CYAN=; GREEN=; YELLOW=; RESET=; fi
info() { printf '%s%s%s\n' "$CYAN" "$*" "$RESET"; }
warn() { printf '%sWARNING: %s%s\n' "$YELLOW" "$*" "$RESET" >&2; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }
mb() { awk -v b="$1" 'BEGIN { printf "%.1f", b / 1048576 }'; }

usage() {
    cat <<EOF
Usage: $(basename "$0") [options]

  --project-name NAME   Cloudflare Pages project name. Served at https://NAME.pages.dev.
                        Default: $PROJECT_NAME
  --build               Rebuild the web player with the unity CLI before deploying.
  --branch NAME         Pages branch. "main" updates the production URL. Any other name makes a
                        preview at https://NAME.<project>.pages.dev and leaves production alone.
                        Default: $BRANCH
  --message TEXT        Note attached to the deployment in the Cloudflare dashboard.
  --bucket NAME         R2 bucket for files over the Pages limit. Created on first use.
                        Default: $BUCKET
  --data-base-url URL   Public base URL of the bucket, for a custom domain connected to it. When
                        omitted, the bucket's r2.dev URL is used (rate-limited; fine for testing).
  -h, --help            Show this help.

Examples:
  ./deploy-web.sh
  ./deploy-web.sh --build --message "Added birds"
  ./deploy-web.sh --branch preview
EOF
}

while (($#)); do
    case "$1" in
        --build) BUILD=1 ;;
        --project-name | --branch | --message | --bucket | --data-base-url)
            (($# >= 2)) || die "$1 needs a value."
            case "$1" in
                --project-name) PROJECT_NAME="$2" ;;
                --branch) BRANCH="$2" ;;
                --message) MESSAGE="$2" ;;
                --bucket) BUCKET="$2" ;;
                --data-base-url) DATA_BASE_URL="$2" ;;
            esac
            shift ;;
        -h | --help) usage; exit 0 ;;
        *) usage >&2; die "Unknown option: $1" ;;
    esac
    shift
done

# These names go into regular expressions below, so keep them to what Cloudflare allows anyway.
[[ "$PROJECT_NAME" =~ ^[a-z0-9-]+$ ]] || die "Project name '$PROJECT_NAME' must be lowercase letters, digits and dashes."
[[ "$BUCKET" =~ ^[a-z0-9-]+$ ]] || die "Bucket name '$BUCKET' must be lowercase letters, digits and dashes."

command -v npx >/dev/null || die "npx not found. Install Node.js 18 or later from https://nodejs.org."

wrangler() { npx --yes wrangler@4 "$@"; }
# Runs wrangler and fails the script with a readable message if it exits non-zero.
run_wrangler() { wrangler "$@" || die "wrangler $* failed (exit $?)"; }
# Runs wrangler and prints stdout and stderr together, whatever the exit code (for probing state).
probe_wrangler() { wrangler "$@" 2>&1 || true; }

if ((BUILD)); then
    command -v unity >/dev/null || die "unity CLI not found. Install it and sign in, then try again."
    info "Building the web player (this takes a few minutes)..."
    mkdir -p "$ROOT/Builds"
    unity build "$ROOT" \
        --profile "Assets/Settings/Build Profiles/WebGL.asset" \
        --output-path "$WEB_DIR" \
        --log-file "$ROOT/Builds/build-web-deploy.log" \
        --no-tail --timeout 3600 ||
        die "Unity build failed. See Builds/build-web-deploy.log."
fi

[[ -f "$WEB_DIR/index.html" ]] || die "No web build at $WEB_DIR. Build it first, or run with --build."

# Stage a clean copy so provenance and log files are not published.
STAGE="$(mktemp -d "${TMPDIR:-/tmp}/washed-ashore-pages.XXXXXX")"
trap 'rm -rf "$STAGE"' EXIT
cp -a "$WEB_DIR/." "$STAGE/"
find "$STAGE" -type f \( -name '*.provenance.json' -o -name '*.log' \) -delete

size_bytes="$(find "$STAGE" -type f -printf '%s\n' | awk '{ s += $1 } END { print s + 0 }')"
built="$(find "$STAGE" -type f -printf '%TY-%Tm-%Td %TH:%TM\n' | sort | tail -n 1)"
info "Deploying build from $built ($(mb "$size_bytes") MB)"

# Every player downloads the whole data file before the game starts. Past this size it is time to
# split content out with Addressables.
data_line="$(find "$STAGE" -type f -name '*.data*' -printf '%s %f\n' | awk '$1 > max { max = $1; line = $0 } END { print line }')"
if [[ -n "$data_line" ]] && ((${data_line%% *} > DATA_WARN_LIMIT)); then
    warn "${data_line#* } is $(mb "${data_line%% *}") MB. Time to plan the Addressables split: see _bmad-output/planning-artifacts/deferred-addressables-content-streaming.md"
fi

# Log in on first use. CLOUDFLARE_API_TOKEN, if set, skips the browser login.
if ! probe_wrangler whoami | grep -q "logged in"; then
    info "Logging in to Cloudflare..."
    run_wrangler login
fi

# Pages rejects any single file over 25 MiB, so those go to R2 instead.
mapfile -d '' too_big < <(find "$STAGE" -type f -size +"${PAGES_LIMIT}c" -print0)
if ((${#too_big[@]})); then
    buckets="$(probe_wrangler r2 bucket list)"
    if grep -q "10042" <<<"$buckets"; then
        die "R2 is not enabled on this Cloudflare account. Enable it once in the dashboard (R2 Object Storage), then run this again."
    fi
    if ! grep -Eq "(^|[[:space:]])$BUCKET[[:space:]]*$" <<<"$buckets"; then
        info "Creating R2 bucket '$BUCKET'..."
        run_wrangler r2 bucket create "$BUCKET"
    fi

    # The game page and the bucket are different origins, so the bucket needs CORS.
    cors="$STAGE.r2-cors.json"
    trap 'rm -rf "$STAGE" "$cors"' EXIT
    printf '%s' '{"rules":[{"allowed":{"origins":["*"],"methods":["GET","HEAD"],"headers":["*"]},"maxAgeSeconds":86400}]}' >"$cors"
    run_wrangler r2 bucket cors set "$BUCKET" --file "$cors" --force

    r2dev_re='https://pub-[a-z0-9]+\.r2\.dev'
    if [[ -z "$DATA_BASE_URL" ]]; then
        dev_url="$(probe_wrangler r2 bucket dev-url get "$BUCKET")"
        if ! grep -Eq "$r2dev_re" <<<"$dev_url"; then
            run_wrangler r2 bucket dev-url enable "$BUCKET" --force
            dev_url="$(probe_wrangler r2 bucket dev-url get "$BUCKET")"
        fi
        DATA_BASE_URL="$(grep -Eo "$r2dev_re" <<<"$dev_url" | head -n 1 || true)"
        [[ -n "$DATA_BASE_URL" ]] || die "Could not read the r2.dev URL for bucket '$BUCKET'."
    fi
    while [[ "$DATA_BASE_URL" == */ ]]; do DATA_BASE_URL="${DATA_BASE_URL%/}"; done

    index_path="$STAGE/index.html"
    # The trailing "x" keeps command substitution from trimming the file's final newlines.
    html="$(cat "$index_path"; printf x)"
    html="${html%x}"
    for file in "${too_big[@]}"; do
        bytes="$(stat -c %s "$file")"
        name="${file##*/}"
        ((bytes <= R2_UPLOAD_LIMIT)) || die "$name is over 300 MB, the largest file Wrangler can upload to R2."
        rel="${file#"$STAGE"/}"
        [[ "$html" == *"\"$rel\""* ]] || die "index.html does not reference $rel, so it cannot be moved to R2."

        # The hash in the key means a new build never collides with a cached old file.
        hash="$(sha256sum "$file" | cut -c1-16)"
        key="$hash/$name"
        info "Uploading $rel ($(mb "$bytes") MB) to R2..."
        run_wrangler r2 object put "$BUCKET/$key" --file "$file" --remote \
            --content-type application/octet-stream \
            --cache-control "public, max-age=31536000, immutable"

        html="${html//"\"$rel\""/"\"$DATA_BASE_URL/$key\""}"
        rm -f "$file"
    done
    printf '%s' "$html" >"$index_path"
fi

# Create the Pages project the first time.
projects="$(probe_wrangler pages project list)"
if ! grep -Eq "\b$PROJECT_NAME\b" <<<"$projects"; then
    info "Creating Pages project '$PROJECT_NAME'..."
    run_wrangler pages project create "$PROJECT_NAME" --production-branch main
fi

run_wrangler pages deploy "$STAGE" \
    --project-name "$PROJECT_NAME" \
    --branch "$BRANCH" \
    --commit-message "$MESSAGE" \
    --commit-dirty=true

# Cloudflare adds a random suffix when the subdomain is taken, so read the real one.
projects="$(probe_wrangler pages project list)"
domain="$(grep -Eo "$PROJECT_NAME[a-z0-9-]*\.pages\.dev" <<<"$projects" | head -n 1 || true)"
domain="${domain:-$PROJECT_NAME.pages.dev}"
if [[ "$BRANCH" == "main" ]]; then url="https://$domain"; else url="https://$BRANCH.$domain"; fi
echo
printf '%sLive at %s%s\n' "$GREEN" "$url" "$RESET"
echo "(A brand-new project can take a minute or two before the URL resolves.)"
