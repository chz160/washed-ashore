# Builds the MC-39 lookout tower headless from the parameter table (see build_mc39.py, owned by technical-artist).
# One command, no UI, repeatable. Output goes to a staging folder outside Assets/; -IntoAssets copies it into the
# Unity project afterwards (that triggers an import, so use it only inside an Editor baton slot).
# Usage: pwsh tools/blender/lookout/build_mc39.ps1 [-Out <dir>] [-Params <json>] [-IntoAssets -AssetsDir <Assets/...>] [-Blender <blender.exe>]
#        -NoTextures builds the grey model only (passes --no-textures; staging only, never -IntoAssets).
#        -WholeMillimetreReach is a comparison build (passes --whole-millimetre-reach; default -Out out/compare-whole-mm).
# Exit codes: 0 built; 1 build failed (Blender/Python error, no OK line, no FBX, timeout);
#             2 environment (Blender missing or not 5.2.2, script or table missing or invalid);
#             3 refused output path.
param(
    [string]$Blender = "",
    [string]$Params = "",
    [string]$Out = "",
    [switch]$IntoAssets,
    [string]$AssetsDir = "",
    [string]$Log = "",
    [string]$Script = "",
    [switch]$NoTextures,
    [switch]$WholeMillimetreReach,
    [int]$TimeoutSec = 3600
)
$ErrorActionPreference = "Stop"
$RequiredVersion = "5.2.2"
$FallbackBlender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"

function Fail([int]$code, [string]$msg) { Write-Host "BUILD_MC39_PS FAILED ($code): $msg"; exit $code }
function FullPath([string]$p, [string]$base) {
    if (-not [System.IO.Path]::IsPathRooted($p)) { $p = Join-Path $base $p }
    return [System.IO.Path]::GetFullPath($p).TrimEnd('\', '/')
}
function IsUnder([string]$path, [string]$root) {
    return ($path -eq $root) -or $path.StartsWith($root + "\", [System.StringComparison]::OrdinalIgnoreCase)
}
function Sha([string]$path) { return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() }
function RelFiles([string]$dir) {
    $list = @(Get-ChildItem -LiteralPath $dir -Recurse -File -Force | ForEach-Object { $_.FullName.Substring($dir.Length + 1).Replace('\', '/') })
    [System.Array]::Sort($list, [System.StringComparer]::Ordinal)
    return $list
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path.TrimEnd('\')
$stageRoot = Join-Path $PSScriptRoot "out"
if (-not $Script) { $Script = Join-Path $PSScriptRoot "build_mc39.py" }
if (-not $Params) { $Params = Join-Path $PSScriptRoot "mc39_params.json" }
if (-not $Out) { $Out = Join-Path $stageRoot $(if ($WholeMillimetreReach) { "compare-whole-mm" } else { "build" }) }
# A comparison run keeps its own log and receipt, so it never replaces those of the build before it.
if (-not $Log) { $Log = Join-Path $stageRoot $(if ($WholeMillimetreReach) { "build_mc39.compare-whole-mm.log" } else { "build_mc39.log" }) }
$Script = FullPath $Script $repo
$Params = FullPath $Params $repo
$Out = FullPath $Out $repo
$Log = FullPath $Log $repo
$assets = if ($AssetsDir) { FullPath $AssetsDir $repo } else { "" }

# --- environment -----------------------------------------------------------------------------------------------
if (-not $Blender) {
    $cmd = Get-Command blender -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($cmd) { $Blender = $cmd.Source } else { $Blender = $FallbackBlender }
}
if (-not (Test-Path -LiteralPath $Blender -PathType Leaf)) { Fail 2 "Blender not found at '$Blender' (Blender $RequiredVersion expected)" }
if (-not (Test-Path -LiteralPath $Script -PathType Leaf)) { Fail 2 "build script not found: $Script" }
if (-not (Test-Path -LiteralPath $Params -PathType Leaf)) { Fail 2 "parameter table not found: $Params" }
try { Get-Content -LiteralPath $Params -Raw | ConvertFrom-Json | Out-Null } catch { Fail 2 "parameter table is not valid JSON: $Params ($($_.Exception.Message))" }

$ErrorActionPreference = "Continue"
$versionLine = (& $Blender -b --version 2>$null | Select-Object -First 1)
$ErrorActionPreference = "Stop"
if ("$versionLine" -notmatch "^Blender $([regex]::Escape($RequiredVersion))(\s|$)") { Fail 2 "Blender $RequiredVersion required, found '$versionLine' at '$Blender'" }

# --- output path rules -----------------------------------------------------------------------------------------
foreach ($unityDir in "Assets", "Packages", "ProjectSettings", "Library") {
    if (IsUnder $Out (Join-Path $repo $unityDir)) { Fail 3 "-Out must be a staging folder outside $unityDir/ (use -IntoAssets to copy into the project): $Out" }
}
if ($IntoAssets -and $NoTextures) { Fail 3 "-NoTextures is a grey-model build and cannot be combined with -IntoAssets" }
if ($IntoAssets -and $WholeMillimetreReach) { Fail 3 "-WholeMillimetreReach is a comparison build and cannot be combined with -IntoAssets" }
# A comparison build may not land where builds of record or the artist's working copy live.
if ($WholeMillimetreReach) {
    foreach ($reserved in "build", "dev") {
        if (IsUnder $Out (Join-Path $stageRoot $reserved)) { Fail 3 "-WholeMillimetreReach may not write into out/$reserved (use out/compare-whole-mm or another folder): $Out" }
    }
}
if ($IntoAssets) {
    if (-not $AssetsDir) { Fail 3 "-IntoAssets needs -AssetsDir <folder under Assets/>" }
    if (-not (IsUnder $assets (Join-Path $repo "Assets")) -or $assets -eq (Join-Path $repo "Assets")) { Fail 3 "-AssetsDir must be a folder under Assets/: $assets" }
    Write-Host "BUILD_MC39_PS WARNING: -IntoAssets writes to $assets and triggers a Unity import. Run it only while you hold the Editor baton."
}
if (IsUnder $Log $Out) { Fail 3 "-Log must be outside -Out (every file in -Out is a build output): $Log" }
if ((Test-Path -LiteralPath $Out) -and @(Get-ChildItem -LiteralPath $Out -Force).Count -gt 0) {
    # Only folders this tooling owns are emptied; anything else has to be empty already.
    $owned = (IsUnder $Out $stageRoot) -or (IsUnder $Out (Join-Path $repo "TestResults"))
    if (-not $owned -or $Out -eq $stageRoot) { Fail 3 "-Out is not empty and is not under tools/blender/lookout/out/ or TestResults/: $Out" }
    Remove-Item -LiteralPath $Out -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $Log) | Out-Null

# --- run Blender -----------------------------------------------------------------------------------------------
# The FBX exporter derives node IDs from Python's string hash; a fixed seed keeps rebuilds byte-identical apart from
# the FBX creation timestamp. Blender ignores PYTHON* variables without --python-use-system-env, so the other
# PYTHON* variables are cleared to keep the user's Python setup out of the build.
$envSet = @{ PYTHONHASHSEED = "0"; PYTHONNOUSERSITE = "1"; PYTHONDONTWRITEBYTECODE = "1"; PYTHONPATH = $null; PYTHONHOME = $null; PYTHONSTARTUP = $null }
$envOld = @{}
foreach ($k in $envSet.Keys) { $envOld[$k] = [Environment]::GetEnvironmentVariable($k, "Process"); [Environment]::SetEnvironmentVariable($k, $envSet[$k], "Process") }

$stdout = "$Log.stdout.tmp"; $stderr = "$Log.stderr.tmp"
$argList = @("-b", "--factory-startup", "-noaudio", "--python-use-system-env", "--python-exit-code", "1",
    "--python", "`"$Script`"", "--", "--params", "`"$Params`"", "--out", "`"$Out`"") -join " "
# Grey-model build for the look gate: geometry only, the texture step is skipped. Not the shipped build.
if ($NoTextures) { $argList += " --no-textures" }
# Comparison build for technical-director only (reach figures in whole millimetres). Never a build of record.
if ($WholeMillimetreReach) { $argList += " --whole-millimetre-reach" }
# build_mc39.py imports its sibling mc39_*.py modules, so those are inputs (render and camera-fit scripts are not).
$pyInputs = [ordered]@{}
foreach ($py in (Get-ChildItem -LiteralPath (Split-Path $Script) -Filter *.py -File | Where-Object { $_.FullName -eq $Script -or $_.Name -like "mc39_*.py" } | Sort-Object Name)) { $pyInputs[$py.Name] = (Sha $py.FullName) }
$started = Get-Date
$timedOut = $false
try {
    $proc = Start-Process -FilePath $Blender -ArgumentList $argList -NoNewWindow -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $null = $proc.Handle   # keeps ExitCode readable after the process ends
    if (-not $proc.WaitForExit($TimeoutSec * 1000)) { $timedOut = $true; $proc.Kill(); $proc.WaitForExit() }
    $exitCode = $proc.ExitCode
} finally {
    foreach ($k in $envOld.Keys) { [Environment]::SetEnvironmentVariable($k, $envOld[$k], "Process") }
}
$finished = Get-Date

$outLines = @(Get-Content -LiteralPath $stdout -ErrorAction SilentlyContinue)
$errLines = @(Get-Content -LiteralPath $stderr -ErrorAction SilentlyContinue)
@("# build_mc39.ps1 $($started.ToString('o'))", "# blender: $Blender ($versionLine)", "# args: $argList", "# exit code: $exitCode  timed out: $timedOut",
    "---- stdout ----") + $outLines + @("---- stderr ----") + $errLines | Set-Content -LiteralPath $Log -Encoding UTF8
Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue

$all = $outLines + $errLines
$all | Where-Object { $_ -match "BUILD_MC39|Traceback|^Error" } | ForEach-Object { Write-Host $_ }
$reasons = @()
if ($timedOut) { $reasons += "timed out after $TimeoutSec s" }
if ($exitCode -ne 0) { $reasons += "Blender exit code $exitCode" }
if ($all -match "Traceback \(most recent call last\)") { $reasons += "Python traceback in the log" }
if ($all -match "BUILD_MC39 FAILED") { $reasons += "script reported BUILD_MC39 FAILED" }
if (-not ($all -match "^BUILD_MC39 OK")) { $reasons += "no 'BUILD_MC39 OK' line" }
$files = RelFiles $Out
if (-not ($files -match "\.fbx$")) { $reasons += "no .fbx in $Out" }
# Import closure: the build prints the .py files it loaded from its own folder ("BUILD_MC39 MODULES a.py,b.py").
# Every one of them has to be on the hashed input list, so a new helper file cannot become an unhashed input.
$closureChecked = $false
$modulesLine = $all | Where-Object { $_ -match "^BUILD_MC39 MODULES " } | Select-Object -Last 1
if ($modulesLine) {
    $closureChecked = $true
    $loaded = @(("$modulesLine" -replace "^BUILD_MC39 MODULES ", "").Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $unhashed = @($loaded | Where-Object { -not $pyInputs.Contains($_) })
    if ($unhashed.Count -gt 0) { $reasons += "the build imported file(s) that are not hashed inputs: $($unhashed -join ', ')" }
}
# Lane-data digest: the build prints "BUILD_MC39 LANE_DATA_DIGEST <sha256>", a hash over every value the climb depends
# on and nothing visual. It goes into the receipt; a build of record without one cannot be compared with the lane data
# of record, so that is a failure.
$laneDataDigest = $null
$digestLine = $all | Where-Object { $_ -match "^BUILD_MC39 LANE_DATA_DIGEST " } | Select-Object -Last 1
if ($digestLine) {
    $laneDataDigest = ("$digestLine" -replace "^BUILD_MC39 LANE_DATA_DIGEST ", "").Trim().ToLowerInvariant()
    if ($laneDataDigest -notmatch "^[0-9a-f]{64}$") { $reasons += "LANE_DATA_DIGEST is not a SHA-256 (64 hex digits): '$laneDataDigest'"; $laneDataDigest = $null }
} elseif (-not ($NoTextures -or $WholeMillimetreReach)) { $reasons += "no 'BUILD_MC39 LANE_DATA_DIGEST' line (required for a build of record)" }
# The same for the hatch, sash and firefinder data ("BUILD_MC39 INTERACTION_DATA_DIGEST <sha256>"), by the same rule.
$interactionDataDigest = $null
$interactionLine = $all | Where-Object { $_ -match "^BUILD_MC39 INTERACTION_DATA_DIGEST " } | Select-Object -Last 1
if ($interactionLine) {
    $interactionDataDigest = ("$interactionLine" -replace "^BUILD_MC39 INTERACTION_DATA_DIGEST ", "").Trim().ToLowerInvariant()
    if ($interactionDataDigest -notmatch "^[0-9a-f]{64}$") { $reasons += "INTERACTION_DATA_DIGEST is not a SHA-256 (64 hex digits): '$interactionDataDigest'"; $interactionDataDigest = $null }
} elseif (-not ($NoTextures -or $WholeMillimetreReach)) { $reasons += "no 'BUILD_MC39 INTERACTION_DATA_DIGEST' line (required for a build of record)" }
# Test overrides: the build prints "BUILD_MC39 TEST_OVERRIDES all_zero=true|false". Anything but all zero is a negative
# control: never a build of record and never copied into Assets/. A build of record must print the line.
$testOverridesAllZero = $null
$overridesLine = $all | Where-Object { $_ -match "^BUILD_MC39 TEST_OVERRIDES " } | Select-Object -Last 1
if ($overridesLine) {
    if ("$overridesLine" -match "^BUILD_MC39 TEST_OVERRIDES all_zero=(true|false)\s*$") { $testOverridesAllZero = ($Matches[1] -eq "true") }
    else { $reasons += "TEST_OVERRIDES line is not 'all_zero=true' or 'all_zero=false': '$overridesLine'" }
    if ($testOverridesAllZero -eq $false -and -not ($NoTextures -or $WholeMillimetreReach)) { $reasons += "test overrides are not all zero (a negative-control table cannot be a build of record)" }
} elseif (-not ($NoTextures -or $WholeMillimetreReach)) { $reasons += "no 'BUILD_MC39 TEST_OVERRIDES' line (required for a build of record)" }
$buildOfRecord = -not ($NoTextures -or $WholeMillimetreReach) -and ($testOverridesAllZero -eq $true) -and ($reasons.Count -eq 0)

$receipt = [ordered]@{
    ok = ($reasons.Count -eq 0); reasons = $reasons; noTextures = [bool]$NoTextures; wholeMillimetreReach = [bool]$WholeMillimetreReach; buildOfRecord = $buildOfRecord; testOverridesAllZero = $testOverridesAllZero; importClosureChecked = $closureChecked; laneDataDigest = $laneDataDigest; interactionDataDigest = $interactionDataDigest
    started = $started.ToString("o"); finished = $finished.ToString("o"); seconds = [math]::Round(($finished - $started).TotalSeconds, 1)
    blender = $Blender; blenderVersion = "$versionLine"; exitCode = $exitCode
    inputs = [ordered]@{ script = $Script; params = $Params; paramsSha256 = (Sha $Params); python = $pyInputs }
    environment = [ordered]@{ PYTHONHASHSEED = "0"; blenderArgs = $argList
        note = "Set by build_mc39.ps1 itself, not inherited. FBX node IDs come from Python hash(), so a bare 'blender -b --python build_mc39.py' gives different node IDs; this wrapper is the only supported way to build." }
    out = $Out
    outputs = @($files | ForEach-Object { [ordered]@{ file = $_; bytes = (Get-Item -LiteralPath (Join-Path $Out $_)).Length; sha256 = (Sha (Join-Path $Out $_)) } })
    log = $Log
}
$receiptPath = [System.IO.Path]::ChangeExtension($Log, ".receipt.json")
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receiptPath -Encoding UTF8

if ($reasons.Count -gt 0) {
    $all | Select-Object -Last 40 | ForEach-Object { Write-Host $_ }
    Fail 1 ("$($reasons -join '; '). Log: $Log")
}
Write-Host "BUILD_MC39_PS OK out=$Out files=$($files.Count) log=$Log"

# --- optional: land in the Unity project (baton slot only) -----------------------------------------------------
if ($IntoAssets) {
    if ($testOverridesAllZero -ne $true) { Fail 3 "-IntoAssets refused: test overrides are not reported as all zero; nothing copied" }
    # Copy only what changed and never delete: .meta files (GUIDs) stay, and an unchanged build causes no reimport.
    $copied = 0
    foreach ($f in $files) {
        $src = Join-Path $Out $f; $dst = Join-Path $assets $f
        if ((Test-Path -LiteralPath $dst) -and ((Sha $dst) -eq (Sha $src))) { continue }
        New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
        Copy-Item -LiteralPath $src -Destination $dst -Force
        $copied++
    }
    Write-Host "BUILD_MC39_PS ASSETS dir=$assets copied=$copied unchanged=$($files.Count - $copied)"
}
exit 0
