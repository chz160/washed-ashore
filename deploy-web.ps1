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

.PARAMETER ProjectName
    Cloudflare Pages project name. The site is served at https://<name>.pages.dev.

.PARAMETER Build
    Rebuild the web player with the unity CLI before deploying.

.PARAMETER Branch
    Pages branch. "main" updates the production URL. Any other name makes a
    preview at https://<branch>.<name>.pages.dev and leaves production alone.

.PARAMETER Message
    Note attached to the deployment in the Cloudflare dashboard.

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
    [string]$Message = "Web build $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
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

# Pages rejects any single file over 25 MiB.
$tooBig = Get-ChildItem $stage -Recurse -File | Where-Object Length -gt 25MB
if ($tooBig) {
    $tooBig | ForEach-Object { Write-Host ("  {0}  {1:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
    throw "These files exceed the 25 MiB Cloudflare Pages limit."
}
$sizeMb = (Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
$built = (Get-ChildItem $stage -Recurse -File | Sort-Object LastWriteTime | Select-Object -Last 1).LastWriteTime
Write-Host ("Deploying build from {0:yyyy-MM-dd HH:mm} ({1:N1} MB)" -f $built, $sizeMb) -ForegroundColor Cyan

# Log in on first use. CLOUDFLARE_API_TOKEN, if set, skips the browser login.
$whoami = (& npx.cmd @wrangler whoami 2>&1) -join "`n"
if ($whoami -notmatch "logged in") {
    Write-Host "Logging in to Cloudflare..." -ForegroundColor Cyan
    Invoke-Wrangler login
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
