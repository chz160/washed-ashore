<#
.SYNOPSIS
    Deploys the Unity web build (Builds/Web) to Cloudflare Pages.

.DESCRIPTION
    Copies Builds/Web to a staging folder (leaving out build provenance files),
    checks it against the Pages limits, and uploads it with Wrangler. The first
    run opens a browser so you can log in to Cloudflare, and creates the Pages
    project if it does not exist yet.

    The build uses Brotli with Unity's decompression fallback, so the loader
    decompresses in JavaScript and Pages needs no special headers.

    Pages rejects any file over 25 MiB. Larger files (in practice Web.data) go
    to an R2 bucket instead, under a key made from the file's hash, and
    index.html is rewritten to load them from there. R2 must be enabled on the
    Cloudflare account once, in the dashboard. The long-term fix is splitting
    content with Addressables; see
    _bmad-output/planning-artifacts/deferred-addressables-content-streaming.md.

.PARAMETER ProjectName
    Cloudflare Pages project name. The site is served at https://<name>.pages.dev.

.PARAMETER Build
    Rebuild the web player with the unity CLI before deploying.

.PARAMETER Branch
    Pages branch. "main" updates the production URL. Any other name makes a
    preview at https://<branch>.<name>.pages.dev and leaves production alone.

.PARAMETER Message
    Note attached to the deployment in the Cloudflare dashboard.

.PARAMETER Bucket
    R2 bucket for files over the Pages limit. Created on first use.

.PARAMETER DataBaseUrl
    Public base URL of the bucket, for a custom domain connected to it. When
    omitted, the bucket's r2.dev URL is used (rate-limited; fine for testing).

.EXAMPLE
    .\deploy-web.ps1
    .\deploy-web.ps1 -Build -Message "Added birds"
    .\deploy-web.ps1 -Branch preview
#>
[CmdletBinding()]
param(
    [string]$ProjectName = "washed-ashore-game",
    [switch]$Build,
    [string]$Branch = "main",
    [string]$Message = "Web build $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
    [string]$Bucket = "washed-ashore-game-data",
    [string]$DataBaseUrl
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$webDir = Join-Path $root "Builds\Web"
$wrangler = @("--yes", "wrangler@4")

function Invoke-Wrangler {
    & npx.cmd @wrangler @args
    if ($LASTEXITCODE -ne 0) { throw "wrangler $($args -join ' ') failed (exit $LASTEXITCODE)" }
}

if (-not (Get-Command npx.cmd -ErrorAction SilentlyContinue)) {
    throw "npx not found. Install Node.js 18 or later from https://nodejs.org."
}

if ($Build) {
    Write-Host "Building the web player (this takes a few minutes)..." -ForegroundColor Cyan
    & unity build $root `
        --profile "Assets/Settings/Build Profiles/WebGL.asset" `
        --output-path $webDir `
        --log-file (Join-Path $root "Builds\build-web-deploy.log") `
        --no-tail --timeout 3600
    if ($LASTEXITCODE -ne 0) { throw "Unity build failed. See Builds\build-web-deploy.log." }
}

if (-not (Test-Path (Join-Path $webDir "index.html"))) {
    throw "No web build at $webDir. Build it first, or run with -Build."
}

# Stage a clean copy so provenance and log files are not published.
$stage = Join-Path ([IO.Path]::GetTempPath()) "washed-ashore-pages"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Copy-Item $webDir $stage -Recurse
Get-ChildItem $stage -Recurse -Include "*.provenance.json", "*.log" | Remove-Item -Force

$sizeMb = (Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
$built = (Get-ChildItem $stage -Recurse -File | Sort-Object LastWriteTime | Select-Object -Last 1).LastWriteTime
Write-Host ("Deploying build from {0:yyyy-MM-dd HH:mm} ({1:N1} MB)" -f $built, $sizeMb) -ForegroundColor Cyan

# Every player downloads the whole data file before the game starts. Past this
# size it is time to split content out with Addressables.
$dataFile = Get-ChildItem $stage -Recurse -File -Filter "*.data*" | Sort-Object Length -Descending | Select-Object -First 1
if ($dataFile -and $dataFile.Length -gt 100MB) {
    Write-Warning ("{0} is {1:N1} MB. Time to plan the Addressables split: see _bmad-output/planning-artifacts/deferred-addressables-content-streaming.md" -f $dataFile.Name, ($dataFile.Length / 1MB))
}

# Log in on first use. CLOUDFLARE_API_TOKEN, if set, skips the browser login.
$whoami = (& npx.cmd @wrangler whoami 2>&1) -join "`n"
if ($whoami -notmatch "logged in") {
    Write-Host "Logging in to Cloudflare..." -ForegroundColor Cyan
    Invoke-Wrangler login
}

# Pages rejects any single file over 25 MiB, so those go to R2 instead.
$tooBig = @(Get-ChildItem $stage -Recurse -File | Where-Object Length -gt 25MB)
if ($tooBig) {
    $buckets = (& npx.cmd @wrangler r2 bucket list 2>&1) -join "`n"
    if ($buckets -match "10042") {
        throw "R2 is not enabled on this Cloudflare account. Enable it once in the dashboard (R2 Object Storage), then run this again."
    }
    if ($buckets -notmatch "(?m)\s$([regex]::Escape($Bucket))\s*$") {
        Write-Host "Creating R2 bucket '$Bucket'..." -ForegroundColor Cyan
        Invoke-Wrangler r2 bucket create $Bucket
    }

    # The game page and the bucket are different origins, so the bucket needs CORS.
    $cors = Join-Path ([IO.Path]::GetTempPath()) "washed-ashore-r2-cors.json"
    '{"rules":[{"allowed":{"origins":["*"],"methods":["GET","HEAD"],"headers":["*"]},"maxAgeSeconds":86400}]}' |
        Set-Content $cors -Encoding ascii
    Invoke-Wrangler r2 bucket cors set $Bucket --file $cors --force

    if (-not $DataBaseUrl) {
        $devUrl = (& npx.cmd @wrangler r2 bucket dev-url get $Bucket 2>&1) -join "`n"
        if ($devUrl -notmatch "https://pub-[a-z0-9]+\.r2\.dev") {
            Invoke-Wrangler r2 bucket dev-url enable $Bucket --force
            $devUrl = (& npx.cmd @wrangler r2 bucket dev-url get $Bucket 2>&1) -join "`n"
        }
        if ($devUrl -notmatch "https://pub-[a-z0-9]+\.r2\.dev") { throw "Could not read the r2.dev URL for bucket '$Bucket'." }
        $DataBaseUrl = $Matches[0]
    }
    $DataBaseUrl = $DataBaseUrl.TrimEnd("/")

    $indexPath = Join-Path $stage "index.html"
    $html = [IO.File]::ReadAllText($indexPath)
    foreach ($file in $tooBig) {
        if ($file.Length -gt 300MB) { throw "$($file.Name) is over 300 MB, the largest file Wrangler can upload to R2." }
        $rel = $file.FullName.Substring($stage.Length + 1).Replace("\", "/")
        if (-not $html.Contains("`"$rel`"")) { throw "index.html does not reference $rel, so it cannot be moved to R2." }

        # The hash in the key means a new build never collides with a cached old file.
        $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.Substring(0, 16).ToLower()
        $key = "$hash/$($file.Name)"
        Write-Host ("Uploading {0} ({1:N1} MB) to R2..." -f $rel, ($file.Length / 1MB)) -ForegroundColor Cyan
        Invoke-Wrangler r2 object put "$Bucket/$key" --file $file.FullName --remote `
            --content-type application/octet-stream `
            --cache-control "public, max-age=31536000, immutable"

        $html = $html.Replace("`"$rel`"", "`"$DataBaseUrl/$key`"")
        Remove-Item $file.FullName -Force
    }
    [IO.File]::WriteAllText($indexPath, $html, [Text.UTF8Encoding]::new($false))
}

# Create the Pages project the first time.
$projects = (& npx.cmd @wrangler pages project list 2>&1) -join "`n"
if ($projects -notmatch "(?m)\b$([regex]::Escape($ProjectName))\b") {
    Write-Host "Creating Pages project '$ProjectName'..." -ForegroundColor Cyan
    Invoke-Wrangler pages project create $ProjectName --production-branch main
}

Invoke-Wrangler pages deploy $stage `
    --project-name $ProjectName `
    --branch $Branch `
    --commit-message $Message `
    --commit-dirty=true

# Cloudflare adds a random suffix when the subdomain is taken, so read the real one.
$projects = (& npx.cmd @wrangler pages project list 2>&1) -join "`n"
$domain = if ($projects -match "$([regex]::Escape($ProjectName))[a-z0-9-]*\.pages\.dev") { $Matches[0] } else { "$ProjectName.pages.dev" }
$url = if ($Branch -eq "main") { "https://$domain" } else { "https://$Branch.$domain" }
Write-Host ""
Write-Host "Live at $url" -ForegroundColor Green
Write-Host "(A brand-new project can take a minute or two before the URL resolves.)"
