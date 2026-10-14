# Smoke import of a lookout tower export into a THROWAWAY Unity project outside this repository. NOT of record: it only
# answers what Unity's FBX importer and TowerImportSettings do with the export (axes, empties' rotation, collider
# proxies, mover pivots, reimport and plain-copy stability). With -Builder <TowerBuilder.cs> the real builder also runs,
# against inert stand-ins for the art and gameplay classes ("not the real rebuild check"). Nothing is written inside
# the repository except the report under TestResults/.
# Starts a Unity Editor in batchmode, so it needs the Editor baton even though the project is outside the repo.
#
# Usage: pwsh tools/blender/lookout/smoke_import.ps1 -Project <new or empty folder outside the repo>
#            -Importer <TowerImportSettings.cs> [-Builder <TowerBuilder.cs>]
#            [-Export tools/blender/lookout/out/dev] [-Report TestResults/tower-tools/smoke/report.json]
# The export is copied read-only: its files are hashed before and after, and `git status` of the repository is recorded
# before and after; all of it goes into <report>.run.json. A changed export is a failure; a changed git status is
# flagged (other agents may be writing their own files meanwhile).
# Exit codes: 0 report written; 1 Unity failed, wrote no report, or the export changed; 2 environment; 3 refused path.
param(
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$Importer,
    [string]$Builder = "",   # TowerBuilder.cs: when given, the real builder also runs, against the inert smoke stand-ins
    [string]$Export = "tools/blender/lookout/out/dev",
    [string]$Report = "TestResults/tower-tools/smoke/report.json",
    [string]$Unity = "",
    [int]$TimeoutSec = 1200
)
$ErrorActionPreference = "Stop"
function Fail([int]$code, [string]$msg) { Write-Host "SMOKE_IMPORT FAILED ($code): $msg"; exit $code }
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path.TrimEnd('\')
function FullPath([string]$p) { if (-not [System.IO.Path]::IsPathRooted($p)) { $p = Join-Path $repo $p }; return [System.IO.Path]::GetFullPath($p).TrimEnd('\', '/') }
function Sha([string]$p) { return (Get-FileHash -Algorithm SHA256 -LiteralPath $p).Hash.ToLowerInvariant() }
$Project = FullPath $Project; $Importer = FullPath $Importer; $Export = FullPath $Export; $Report = FullPath $Report
function ExportHashes() { $h = [ordered]@{}; foreach ($f in (Get-ChildItem -LiteralPath $Export -File | Sort-Object Name)) { $h[$f.Name] = (Sha $f.FullName) }; return $h }
function RepoStatus() { return @(git -C $repo status --porcelain 2>$null | ForEach-Object { "$_" }) }

$versionLine = Get-Content -LiteralPath (Join-Path $repo "ProjectSettings\ProjectVersion.txt") | Where-Object { $_ -match "^m_EditorVersion:" } | Select-Object -First 1
$version = ("$versionLine" -replace "^m_EditorVersion:\s*", "").Trim()
if (-not $Unity) { $Unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe" }
if (-not (Test-Path -LiteralPath $Unity -PathType Leaf)) { Fail 2 "Unity $version not found at '$Unity'" }
if (-not (Test-Path -LiteralPath $Importer -PathType Leaf)) { Fail 2 "importer source not found: $Importer" }
if ($Builder) { $Builder = FullPath $Builder; if (-not (Test-Path -LiteralPath $Builder -PathType Leaf)) { Fail 2 "builder source not found: $Builder" } }
foreach ($f in "MC39_Lookout.fbx", "MC39_Lookout.manifest.json") { if (-not (Test-Path -LiteralPath (Join-Path $Export $f))) { Fail 2 "export has no ${f}: $Export" } }
if ($Project -eq $repo -or $Project.StartsWith($repo + "\", [System.StringComparison]::OrdinalIgnoreCase)) { Fail 3 "-Project must be outside the repository: $Project" }
if ((Test-Path -LiteralPath $Project) -and @(Get-ChildItem -LiteralPath $Project -Force).Count -gt 0) { Fail 3 "-Project must be a new or empty folder (nothing is ever deleted by this script): $Project" }
if (Test-Path -LiteralPath $Report) { Fail 3 "report already exists, move it first: $Report" }
if (Get-Process Unity -ErrorAction SilentlyContinue) { Fail 2 "a Unity Editor is already running; this needs the baton and a free Editor" }

$exportBefore = ExportHashes
$statusBefore = RepoStatus

# --- the throwaway project: same Editor version and layers as the game, the files under test, the report script -----
$lookout = Join-Path $Project "Assets\World\Lookout"
$editor = Join-Path $Project "Assets\Editor\Lookout"
foreach ($d in $lookout, $editor, (Join-Path $Project "Assets\Scripts\World"), (Join-Path $Project "ProjectSettings"), (Join-Path $Project "Packages")) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
Copy-Item -LiteralPath (Join-Path $repo "ProjectSettings\ProjectVersion.txt") -Destination (Join-Path $Project "ProjectSettings\ProjectVersion.txt")
Copy-Item -LiteralPath (Join-Path $repo "ProjectSettings\TagManager.asset") -Destination (Join-Path $Project "ProjectSettings\TagManager.asset")
$packages = '{ "dependencies": { "com.unity.modules.physics": "1.0.0", "com.unity.modules.imageconversion": "1.0.0", "com.unity.modules.jsonserialize": "1.0.0", "com.unity.modules.animation": "1.0.0", "com.unity.modules.terrain": "1.0.0" } }'
[System.IO.File]::WriteAllText((Join-Path $Project "Packages\manifest.json"), $packages)
Copy-Item -LiteralPath $Importer -Destination (Join-Path $editor "TowerImportSettings.cs")
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "smoke\TowerSmokeReport.cs") -Destination (Join-Path $editor "TowerSmokeReport.cs")
$smokeFiles = @("WorldLayersShim.cs", "TowerSmokeReport.cs")
if ($Builder) {
    # The real builder, unchanged, with inert stand-ins for the art and gameplay classes it calls.
    Copy-Item -LiteralPath $Builder -Destination (Join-Path $editor "TowerBuilder.cs")
    foreach ($f in "SmokeStandIns.cs", "TowerSmokeBuilder.cs") { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "smoke\$f") -Destination (Join-Path $editor $f); $smokeFiles += $f }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "smoke\WorldLayersShim.cs") -Destination (Join-Path $Project "Assets\Scripts\World\WorldLayersShim.cs")
# Smoke-only copy of the export (the build wrapper's -IntoAssets is for builds of record and is not used here).
$copied = @()
foreach ($f in (Get-ChildItem -LiteralPath $Export -File | Where-Object { $_.Extension -in ".fbx", ".png", ".json" } | Sort-Object Name)) {
    Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $lookout $f.Name)
    $copied += $f.Name
}

New-Item -ItemType Directory -Force -Path (Split-Path $Report) | Out-Null
$log = [System.IO.Path]::ChangeExtension($Report, ".editor.log")
$argLine = "-batchmode -nographics -projectPath `"$Project`" -executeMethod TowerSmoke.Run -smokeReport `"$Report`" -logFile `"$log`""
$started = Get-Date
$proc = Start-Process -FilePath $Unity -ArgumentList $argLine -PassThru -NoNewWindow
$null = $proc.Handle
$timedOut = -not $proc.WaitForExit($TimeoutSec * 1000)
if ($timedOut) { $proc.Kill(); $proc.WaitForExit() }
$finished = Get-Date

$exportAfter = ExportHashes
$statusAfter = RepoStatus
$exportSame = (($exportBefore | ConvertTo-Json -Compress) -eq ($exportAfter | ConvertTo-Json -Compress))
$statusSame = (($statusBefore -join "`n") -eq ($statusAfter -join "`n"))
$standIns = [ordered]@{}
foreach ($f in $smokeFiles) { $standIns[$f] = (Sha (Join-Path $PSScriptRoot "smoke\$f")) }
$run = [ordered]@{
    notOfRecord = $true; unity = $Unity; unityVersion = $version; project = $Project
    export = $Export; exportFilesCopied = $copied; exportHashesBefore = $exportBefore; exportHashesAfter = $exportAfter; exportUnchanged = $exportSame
    gitStatusUnchanged = $statusSame; gitStatusBefore = $statusBefore; gitStatusAfter = $statusAfter
    importer = $Importer; importerSha256 = (Sha $Importer); builder = $(if ($Builder) { $Builder } else { "not run" }); builderSha256 = $(if ($Builder) { Sha $Builder }); smokeOnlyFiles = $standIns
    started = $started.ToString("o"); finished = $finished.ToString("o"); seconds = [math]::Round(($finished - $started).TotalSeconds, 1)
    exitCode = $proc.ExitCode; timedOut = $timedOut; report = $Report; editorLog = $log
}
[System.IO.File]::WriteAllText([System.IO.Path]::ChangeExtension($Report, ".run.json"), ($run | ConvertTo-Json -Depth 4))
if ($timedOut) { Fail 1 "Unity timed out after $TimeoutSec s (see $log)" }
if (-not (Test-Path -LiteralPath $Report)) { Fail 1 "Unity exited $($proc.ExitCode) and wrote no report; compile or import errors are in $log" }
if ($proc.ExitCode -ne 0) { Fail 1 "Unity exited $($proc.ExitCode); the report holds the exception: $Report" }
if (-not $exportSame) { Fail 1 "the export folder changed while the smoke import ran (exportHashesBefore/After in run.json)" }
if (-not $statusSame) { Write-Host "SMOKE_IMPORT NOTE: git status of the repository differs before and after (see run.json); this script writes only under TestResults/" }
Write-Host "SMOKE_IMPORT OK report=$Report seconds=$($run.seconds) project=$Project (left in place; delete it by hand when done)"
exit 0
