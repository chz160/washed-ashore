<#
.SYNOPSIS
    Publishes the Windows build (Builds/Windows) as a zip on the GitHub Releases page.

.DESCRIPTION
    Copies Builds/Windows to a staging folder (leaving out the folders Unity
    marks "DoNotShip", build provenance and log files), zips it, and creates a
    GitHub release with the zip attached, using the gh CLI.

    The version is the current time as yyyy.MM.dd.HHmm, for example
    2026.10.06.1430, and the tag is that version with a "v" in front. The tag
    points at the current commit, which must already be pushed.

.PARAMETER Build
    Rebuild the Windows player with the unity CLI before releasing.

.PARAMETER Notes
    Release notes. Defaults to the commit the release is tagged on.

.PARAMETER Prerelease
    Mark the release as a pre-release.

.PARAMETER Draft
    Create the release as a draft, so it is not public until you publish it on GitHub.

.EXAMPLE
    .\release-windows.ps1
    .\release-windows.ps1 -Build -Notes "Added birds"
    .\release-windows.ps1 -Prerelease
#>
[CmdletBinding()]
param(
    [switch]$Build,
    [string]$Notes,
    [switch]$Prerelease,
    [switch]$Draft
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$winDir = Join-Path $root "Builds\Windows"
$repo = "chz160/washed-ashore"
$version = Get-Date -Format "yyyy.MM.dd.HHmm"
$tag = "v$version"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "gh not found. Install the GitHub CLI from https://cli.github.com, then run 'gh auth login'."
}
& gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw "gh is not logged in. Run 'gh auth login' first." }

# The tag is created on GitHub, so the commit has to be there already.
$commit = (& git -C $root rev-parse HEAD).Trim()
if (-not (& git -C $root branch -r --contains $commit)) {
    throw "Commit $($commit.Substring(0, 7)) is not on GitHub yet. Push it first."
}
if (& git -C $root status --porcelain --untracked-files=no) {
    Write-Warning "The working tree has uncommitted changes. The release is tagged on $($commit.Substring(0, 7)), which may not match the build."
}

if ($Build) {
    Write-Host "Building the Windows player (this takes a few minutes)..." -ForegroundColor Cyan
    & unity build $root `
        --target StandaloneWindows64 `
        --output-path (Join-Path $winDir "WashedAshorePOC.exe") `
        --log-file (Join-Path $root "Builds\build-windows-release.log") `
        --no-tail --timeout 3600
    if ($LASTEXITCODE -ne 0) { throw "Unity build failed. See Builds\build-windows-release.log." }
}

$exe = Get-ChildItem $winDir -Filter "*.exe" -File -ErrorAction SilentlyContinue |
    Where-Object Name -ne "UnityCrashHandler64.exe" | Select-Object -First 1
if (-not $exe) {
    throw "No Windows build at $winDir. Build it first, or run with -Build."
}

# Stage a clean copy inside a "WashedAshore" folder, so the zip extracts into one folder.
$stageRoot = Join-Path ([IO.Path]::GetTempPath()) "washed-ashore-release"
$stage = Join-Path $stageRoot "WashedAshore"
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory $stageRoot | Out-Null
Copy-Item $winDir $stage -Recurse
Get-ChildItem $stage -Directory | Where-Object Name -match "DoNotShip|DontShip" | Remove-Item -Recurse -Force
Get-ChildItem $stage -Recurse -Include "*.provenance.json", "*.log" | Remove-Item -Force

$zip = Join-Path $stageRoot "WashedAshore-$version-win64.zip"
Write-Host "Zipping $version..." -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)

# GitHub rejects release assets of 2 GiB or more.
$zipMb = (Get-Item $zip).Length / 1MB
if ($zipMb -ge 2048) { throw ("The zip is {0:N0} MB, over GitHub's 2 GiB limit for a release file." -f $zipMb) }
$built = (Get-ChildItem $stage -Recurse -File | Sort-Object LastWriteTime | Select-Object -Last 1).LastWriteTime
Write-Host ("Releasing {0} from the build of {1:yyyy-MM-dd HH:mm} ({2:N1} MB zipped)" -f $tag, $built, $zipMb) -ForegroundColor Cyan

if (-not $Notes) { $Notes = "Windows build of $(& git -C $root log -1 --format='%h %s' $commit)" }
$releaseArgs = @(
    "release", "create", $tag, $zip,
    "--repo", $repo,
    "--target", $commit,
    "--title", "Washed Ashore $version",
    "--notes", $Notes
)
if ($Prerelease) { $releaseArgs += "--prerelease" }
if ($Draft) { $releaseArgs += "--draft" }
$url = & gh @releaseArgs
if ($LASTEXITCODE -ne 0) { throw "gh release create failed (exit $LASTEXITCODE)." }

Write-Host ""
Write-Host "Released at $url" -ForegroundColor Green
