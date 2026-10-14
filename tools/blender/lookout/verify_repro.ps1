# T3 reproducibility check: two clean runs of build_mc39.ps1 into separate empty folders, then a byte comparison of
# every output file. The bar is byte-identical apart from timestamps.
#
# The only bytes masked are the FBX creation time: the seven int32 values Year, Month, Day, Hour, Minute, Second and
# Millisecond under FBXHeaderExtension/CreationTimeStamp (4 bytes each, 28 bytes per FBX). Blender's exporter fills
# them from datetime.now() (io_scene_fbx/export_fbx_bin.py, fbx_header_elements); the root CreationTime string and
# FileId are fixed constants in encode_bin.py, so they are not masked. The fields are located by walking the FBX
# node records, never by pattern or fixed offset, and a mask only counts if the decoded value is the wall-clock
# time of that run. Any other differing byte, in any file, is a FAIL.
#
# Negative control (-ControlSet "<key>=<value>", dotted key for nested tables): a third build from a copy of the table
# with that one parameter changed has to differ from run-a outside the mask. Without it the check could pass on a
# build that ignores its table, so the verdict is INCOMPLETE until a control has run.
#
# Usage: pwsh tools/blender/lookout/verify_repro.ps1 -ControlSet "<key>=<value>" [-Params <json>] [-ResultDir <dir>]
#        -NoTextures checks the grey-model build (geometry only); the result of record is the full build.
# Writes <ResultDir>/repro.json (default TestResults/tower-tools/), plus run-a/, run-b/ and a log + receipt per run.
#
# -Against <dir> compares an existing copy (for example the folder landed under Assets/; read-only, .meta files
# ignored) with ONE fresh build instead, and writes <ResultDir>/against.json. The existing copy has no run window,
# so its creation time only has to be a valid date that is not in the future.
# Exit codes: 0 PASS; 1 FAIL (outputs differ, or the control did not); 2 a build failed, the inputs changed mid-check,
#             or no negative control was given (INCOMPLETE).
param(
    [string]$Params = "",
    [string]$ResultDir = "",
    [string]$Blender = "",
    [string]$Script = "",
    [string]$Against = "",
    [string]$ControlSet = "",
    [switch]$NoTextures
)
$ErrorActionPreference = "Stop"
$TimeFields = "Year", "Month", "Day", "Hour", "Minute", "Second", "Millisecond"
$MaxRanges = 64

Add-Type -TypeDefinition @"
using System.Collections.Generic;
public static class ReproDiff {
    // Differing byte ranges [offset, length] over the common length; stops listing after max but keeps counting.
    public static long[] Ranges(byte[] a, byte[] b, int max, out long ranges, out long bytes) {
        var list = new List<long>(); ranges = 0; bytes = 0;
        long n = a.LongLength < b.LongLength ? a.LongLength : b.LongLength, start = -1;
        for (long i = 0; i <= n; i++) {
            bool diff = i < n && a[i] != b[i];
            if (diff) { bytes++; if (start < 0) start = i; }
            else if (start >= 0) { if (ranges < max) { list.Add(start); list.Add(i - start); } ranges++; start = -1; }
        }
        return list.ToArray();
    }
}
"@

function FullPath([string]$p, [string]$base) {
    if (-not [System.IO.Path]::IsPathRooted($p)) { $p = Join-Path $base $p }
    return [System.IO.Path]::GetFullPath($p).TrimEnd('\', '/')
}
function Sha([string]$path) { return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() }
function ShaBytes([byte[]]$bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.BitConverter]::ToString($sha.ComputeHash($bytes)) -replace "-", "").ToLowerInvariant() } finally { $sha.Dispose() }
}
function RelFiles([string]$dir) {
    $list = @(Get-ChildItem -LiteralPath $dir -Recurse -File -Force | ForEach-Object { $_.FullName.Substring($dir.Length + 1).Replace('\', '/') })
    [System.Array]::Sort($list, [System.StringComparer]::Ordinal)
    return $list
}

# One FBX binary node record at $pos: name, where its properties and children start, where it ends. $null at a sentinel.
function FbxNode([byte[]]$b, [long]$pos, [int]$version) {
    if ($version -lt 7500) {
        $end = [long][System.BitConverter]::ToUInt32($b, $pos); $count = [long][System.BitConverter]::ToUInt32($b, $pos + 4)
        $propLen = [long][System.BitConverter]::ToUInt32($b, $pos + 8); $head = 12
    } else {
        $end = [long][System.BitConverter]::ToUInt64($b, $pos); $count = [long][System.BitConverter]::ToUInt64($b, $pos + 8)
        $propLen = [long][System.BitConverter]::ToUInt64($b, $pos + 16); $head = 24
    }
    if ($end -eq 0) { return $null }
    $nameLen = [int]$b[$pos + $head]
    $props = $pos + $head + 1 + $nameLen
    return @{ name = [System.Text.Encoding]::ASCII.GetString($b, $pos + $head + 1, $nameLen); props = $props; propCount = $count; children = $props + $propLen; end = $end }
}
function FbxChildren([byte[]]$b, [long]$start, [long]$limit, [int]$version) {
    $pos = $start; $nodes = @()
    while ($pos -lt $limit) {
        $n = FbxNode $b $pos $version
        if ($null -eq $n) { break }
        if ($n.end -le $pos -or $n.end -gt $b.LongLength) { throw "corrupt FBX node record at offset $pos" }
        $nodes += $n; $pos = $n.end
    }
    return $nodes
}
# The byte ranges of the FBX creation time, with the values found there.
function FbxTimestampMasks([byte[]]$b) {
    if ([System.Text.Encoding]::ASCII.GetString($b, 0, 18) -ne "Kaydara FBX Binary") { throw "not a binary FBX" }
    $version = [int][System.BitConverter]::ToUInt32($b, 23)
    $header = FbxChildren $b 27 $b.LongLength $version | Where-Object { $_.name -eq "FBXHeaderExtension" } | Select-Object -First 1
    if (-not $header) { throw "FBXHeaderExtension not found" }
    $stamp = FbxChildren $b $header.children $header.end $version | Where-Object { $_.name -eq "CreationTimeStamp" } | Select-Object -First 1
    if (-not $stamp) { throw "CreationTimeStamp not found" }
    $masks = @()
    foreach ($n in (FbxChildren $b $stamp.children $stamp.end $version)) {
        if ($TimeFields -notcontains $n.name) { continue }
        if ($n.propCount -ne 1 -or $b[$n.props] -ne 0x49) { throw "CreationTimeStamp/$($n.name) is not a single int32" }
        $masks += [ordered]@{ field = "FBXHeaderExtension/CreationTimeStamp/$($n.name)"; offset = $n.props + 1; length = 4; value = [System.BitConverter]::ToInt32($b, $n.props + 1) }
    }
    if ($masks.Count -ne $TimeFields.Count) { throw "expected $($TimeFields.Count) CreationTimeStamp fields, found $($masks.Count)" }
    return $masks
}
function StampTime($masks) {
    $v = @{}; foreach ($m in $masks) { $v[$m.field.Split('/')[-1]] = $m.value }
    return New-Object DateTime ($v.Year, $v.Month, $v.Day, $v.Hour, $v.Minute, $v.Second, $v.Millisecond)
}
function Masked([byte[]]$b, $masks) {
    $copy = [byte[]]$b.Clone()
    foreach ($m in $masks) { for ($i = 0; $i -lt $m.length; $i++) { $copy[$m.offset + $i] = 0 } }
    return , $copy
}
function DiffInfo([byte[]]$a, [byte[]]$b) {
    $ranges = [long]0; $bytes = [long]0
    $flat = [ReproDiff]::Ranges($a, $b, $MaxRanges, [ref]$ranges, [ref]$bytes)
    $list = @(); for ($i = 0; $i -lt $flat.Length; $i += 2) { $list += [ordered]@{ offset = $flat[$i]; length = $flat[$i + 1] } }
    return [ordered]@{ differingBytes = $bytes; differingRanges = $ranges; listed = $list.Count; ranges = $list }
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path.TrimEnd('\')
$build = Join-Path $PSScriptRoot "build_mc39.ps1"
if (-not $Script) { $Script = Join-Path $PSScriptRoot "build_mc39.py" }
if (-not $Params) { $Params = Join-Path $PSScriptRoot "mc39_params.json" }
if (-not $ResultDir) { $ResultDir = Join-Path $repo "TestResults\tower-tools" }
$Script = FullPath $Script $repo; $Params = FullPath $Params $repo; $ResultDir = FullPath $ResultDir $repo
New-Item -ItemType Directory -Force -Path $ResultDir | Out-Null
$resultPath = Join-Path $ResultDir $(if ($Against) { "against.json" } else { "repro.json" })
if ($Against) {
    $Against = FullPath $Against $repo
    if (-not (Test-Path -LiteralPath $Against -PathType Container)) { Write-Host "VERIFY_REPRO ERROR -Against folder not found: $Against"; exit 2 }
}
$shell = (Get-Process -Id $PID).Path

function InputHashes() {
    $h = [ordered]@{ "build_mc39.ps1" = (Sha $build); "verify_repro.ps1" = (Sha $PSCommandPath) }
    if (Test-Path -LiteralPath $Params) { $h[(Split-Path $Params -Leaf)] = (Sha $Params) }
    # build_mc39.py imports its sibling mc39_*.py modules, so those are inputs (render and camera-fit scripts are not).
    foreach ($py in (Get-ChildItem -LiteralPath (Split-Path $Script) -Filter *.py -File | Where-Object { $_.FullName -eq $Script -or $_.Name -like "mc39_*.py" } | Sort-Object Name)) { $h[$py.Name] = (Sha $py.FullName) }
    return $h
}

$result = [ordered]@{
    check = $(if ($Against) { "Existing copy vs one clean run of build_mc39.ps1, byte comparison of every output" } else { "T3 reproducibility: two clean runs of build_mc39.ps1, byte comparison of every output" })
    verdict = "FAIL"; reasons = @()
    date = (Get-Date).ToString("o")
    commit = $null
    script = $Script; params = $Params
    inputs = (InputHashes)
    maskPolicy = "Only FBXHeaderExtension/CreationTimeStamp/{$($TimeFields -join ',')} (int32 each, 28 bytes per FBX): Blender writes datetime.now() there. Located by walking the FBX node records; accepted only when the decoded value falls inside that run's wall-clock window. Nothing else is masked."
    maskedBytesPerFbx = 4 * $TimeFields.Count
    noTextures = [bool]$NoTextures
    runs = @(); files = @()
}
try { $result.commit = "$(git -C $repo rev-parse HEAD 2>$null)" } catch { }
function Finish([int]$code) {
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Host "VERIFY_REPRO $($result.verdict) result=$resultPath"
    foreach ($r in $result.reasons) { Write-Host "  - $r" }
    exit $code
}

# --- two clean runs --------------------------------------------------------------------------------------------
$dirs = @()
$names = "run-a", "run-b"
if ($Against) {
    $names = "existing", "run-fresh"
    $result.runs += [ordered]@{ name = "existing"; out = $Against; built = $false }
    $dirs += $Against
}
foreach ($name in ($names | Where-Object { $_ -ne "existing" })) {
    $dir = Join-Path $ResultDir $name; $log = Join-Path $ResultDir "$name.log"
    $buildArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $build, "-Script", $Script, "-Params", $Params, "-Out", $dir, "-Log", $log)
    if ($Blender) { $buildArgs += @("-Blender", $Blender) }
    if ($NoTextures) { $buildArgs += "-NoTextures" }
    & $shell @buildArgs | ForEach-Object { Write-Host "[$name] $_" }
    $code = $LASTEXITCODE
    $receiptPath = [System.IO.Path]::ChangeExtension($log, ".receipt.json")
    $receipt = if (Test-Path -LiteralPath $receiptPath) { Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json } else { $null }
    $result.runs += [ordered]@{ name = $name; out = $dir; log = $log; receipt = $receiptPath; exitCode = $code
        started = $(if ($receipt) { $receipt.started }); finished = $(if ($receipt) { $receipt.finished }); blenderVersion = $(if ($receipt) { $receipt.blenderVersion })
        importClosureChecked = $(if ($receipt) { [bool]$receipt.importClosureChecked } else { $false })
        laneDataDigest = $(if ($receipt) { $receipt.laneDataDigest }); interactionDataDigest = $(if ($receipt) { $receipt.interactionDataDigest }) }
    if ($code -ne 0) { $result.verdict = "ERROR"; $result.reasons += "${name}: build_mc39.ps1 exited $code (see $log)"; Finish 2 }
    $dirs += $dir
}

# --- negative control: one parameter changed ---------------------------------------------------------------------
$controlDir = $null
if ($ControlSet -and -not $Against) {
    $key, $valueText = $ControlSet -split "=", 2
    try { $value = "[$valueText]" | ConvertFrom-Json; if ($value -is [array]) { $value = $value[0] } } catch { $value = $valueText }
    $table = Get-Content -LiteralPath $Params -Raw | ConvertFrom-Json
    $node = $table; $parts = $key.Trim().Split('.')
    for ($i = 0; $i -lt $parts.Count; $i++) {
        if ($null -eq $node -or -not ($node.PSObject.Properties.Name -contains $parts[$i])) { $result.verdict = "ERROR"; $result.reasons += "-ControlSet: key '$key' is not in the parameter table"; Finish 2 }
        if ($i -lt $parts.Count - 1) { $node = $node.($parts[$i]) }
        else { $old = $node.($parts[$i]); $node.($parts[$i]) = $value }
    }
    if ("$old" -eq "$value") { $result.verdict = "ERROR"; $result.reasons += "-ControlSet: '$key' already has the value $value"; Finish 2 }
    $controlParams = Join-Path $ResultDir "control-params.json"
    [System.IO.File]::WriteAllText($controlParams, ($table | ConvertTo-Json -Depth 32), (New-Object System.Text.UTF8Encoding $false))   # no BOM: Python's json rejects one
    $controlDir = Join-Path $ResultDir "run-control"; $log = Join-Path $ResultDir "run-control.log"
    $buildArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $build, "-Script", $Script, "-Params", $controlParams, "-Out", $controlDir, "-Log", $log)
    if ($Blender) { $buildArgs += @("-Blender", $Blender) }
    if ($NoTextures) { $buildArgs += "-NoTextures" }
    & $shell @buildArgs | ForEach-Object { Write-Host "[run-control] $_" }
    $result.negativeControl = [ordered]@{ key = $key; original = $old; changedTo = $value; params = $controlParams; out = $controlDir; log = $log; exitCode = $LASTEXITCODE; differsOutsideMask = $false; files = @() }
    if ($LASTEXITCODE -ne 0) { $result.verdict = "ERROR"; $result.reasons += "run-control: build_mc39.ps1 exited $LASTEXITCODE (see $log)"; Finish 2 }
}
$after = InputHashes
foreach ($k in $after.Keys) {
    if ($after[$k] -ne $result.inputs[$k]) { $result.verdict = "ERROR"; $result.reasons += "input changed during the check: $k"; Finish 2 }
}

# --- compare ---------------------------------------------------------------------------------------------------
$filesA = RelFiles $dirs[0]; $filesB = RelFiles $dirs[1]
if ($Against) { $filesA = @($filesA | Where-Object { $_ -notmatch "\.meta$" }) }
foreach ($f in $filesA) { if ($filesB -notcontains $f) { $result.reasons += "only in $($names[0]): $f" } }
foreach ($f in $filesB) { if ($filesA -notcontains $f) { $result.reasons += "only in $($names[1]): $f" } }
if (-not ($filesA -match "\.fbx$")) { $result.reasons += "no FBX among the outputs" }

foreach ($f in ($filesA | Where-Object { $filesB -contains $_ })) {
    $pa = Join-Path $dirs[0] $f; $pb = Join-Path $dirs[1] $f
    $a = [System.IO.File]::ReadAllBytes($pa); $b = [System.IO.File]::ReadAllBytes($pb)
    $entry = [ordered]@{ file = $f; status = ""; bytesA = $a.LongLength; bytesB = $b.LongLength; sha256A = (ShaBytes $a); sha256B = (ShaBytes $b) }
    $isFbx = $f -match "\.fbx$"
    $masks = $null
    if ($isFbx) {
        try {
            $ma = FbxTimestampMasks $a; $mb = FbxTimestampMasks $b
            $window = @(); $inWindow = $true
            for ($i = 0; $i -lt 2; $i++) {
                $t = StampTime @($ma, $mb)[$i]; $run = $result.runs[$i]
                if ($run.name -eq "existing") { $ok = $t -le (Get-Date).AddSeconds(2) }
                else { $ok = ($t -ge ([DateTime]$run.started).AddSeconds(-2)) -and ($t -le ([DateTime]$run.finished).AddSeconds(2)) }
                if (-not $ok) { $inWindow = $false }
                $window += [ordered]@{ run = $run.name; fbxCreationTime = $t.ToString("yyyy-MM-dd HH:mm:ss.fff"); insideRunWindow = $ok }
            }
            $sameLayout = ($a.LongLength -eq $b.LongLength)
            $fields = @()
            for ($i = 0; $i -lt $ma.Count; $i++) {
                if ($ma[$i].offset -ne $mb[$i].offset -or $ma[$i].field -ne $mb[$i].field) { $sameLayout = $false }
                $fields += [ordered]@{ field = $ma[$i].field; offset = $ma[$i].offset; length = 4; valueA = $ma[$i].value; valueB = $mb[$i].value }
            }
            $entry.timestampFields = $fields; $entry.timestampCheck = $window
            if ($sameLayout -and $inWindow) { $masks = $ma }
            elseif (-not $inWindow) { $entry.maskRejected = "CreationTimeStamp is not the run's wall-clock time, so it is not treated as a timestamp" }
            else { $entry.maskRejected = "file length or timestamp field offsets differ between the runs" }
        } catch { $entry.maskRejected = "FBX parse failed: $($_.Exception.Message)" }
    }
    if ($entry.sha256A -eq $entry.sha256B) {
        $entry.status = "identical"
    } else {
        $entry.rawDiff = DiffInfo $a $b
        if ($masks) {
            $am = Masked $a $masks; $bm = Masked $b $masks
            $entry.maskedSha256A = (ShaBytes $am); $entry.maskedSha256B = (ShaBytes $bm)
            if ($entry.maskedSha256A -eq $entry.maskedSha256B) { $entry.status = "identical apart from the FBX creation timestamp" }
            else { $entry.status = "DIFFERENT"; $entry.diffOutsideTimestamp = DiffInfo $am $bm }
        } else { $entry.status = "DIFFERENT" }
        if ($entry.status -eq "DIFFERENT") {
            $d = if ($entry.diffOutsideTimestamp) { $entry.diffOutsideTimestamp } else { $entry.rawDiff }
            $first = if ($d.ranges.Count -gt 0) { " (first at offset $($d.ranges[0].offset))" } else { "" }
            $sizes = if ($a.LongLength -ne $b.LongLength) { ", sizes $($a.LongLength) vs $($b.LongLength)" } else { "" }
            $result.reasons += "${f}: $($d.differingBytes) byte(s) differ in $($d.differingRanges) range(s) outside any timestamp field$first$sizes"
        }
    }
    Write-Host ("  {0,-46} {1}" -f $f, $entry.status)
    $result.files += $entry
}

if ($controlDir) {
    # Same mask as above: the control only counts if an FBX differs from run-a once the 28 timestamp bytes are zeroed.
    foreach ($f in (RelFiles $controlDir)) {
        $pa = Join-Path $dirs[0] $f
        $c = [System.IO.File]::ReadAllBytes((Join-Path $controlDir $f))
        $entry = [ordered]@{ file = $f; sha256Control = (ShaBytes $c); differsFromRunA = $true }
        if (Test-Path -LiteralPath $pa) {
            $a = [System.IO.File]::ReadAllBytes($pa)
            if ($f -match "\.fbx$") {
                $am = Masked $a (FbxTimestampMasks $a); $cm = Masked $c (FbxTimestampMasks $c)
                $entry.maskedSha256RunA = (ShaBytes $am); $entry.maskedSha256Control = (ShaBytes $cm)
                $entry.differsFromRunA = ($entry.maskedSha256RunA -ne $entry.maskedSha256Control)
                if ($entry.differsFromRunA) { $result.negativeControl.differsOutsideMask = $true }
            } else { $entry.differsFromRunA = ((ShaBytes $a) -ne $entry.sha256Control) }
        }
        $result.negativeControl.files += $entry
    }
    Write-Host ("  negative control ({0}: {1} -> {2}): FBX differs outside the mask = {3}" -f $result.negativeControl.key, $result.negativeControl.original, $result.negativeControl.changedTo, $result.negativeControl.differsOutsideMask)
    if (-not $result.negativeControl.differsOutsideMask) { $result.reasons += "negative control: changing '$($result.negativeControl.key)' did not change the FBX outside the timestamp mask" }
}
# Lane-data digests of the built runs must agree: a cheap early signal beside the byte comparison, not instead of it.
$digests = @($result.runs | Where-Object { $_.Contains("laneDataDigest") } | ForEach-Object { "$($_.laneDataDigest)" })
$result.laneDataDigests = $digests
if ($digests.Count -ge 2 -and @($digests | Select-Object -Unique).Count -gt 1) { $result.reasons += "lane-data digest differs between the runs: $($digests -join ' vs ')" }
$interaction = @($result.runs | Where-Object { $_.Contains("interactionDataDigest") } | ForEach-Object { "$($_.interactionDataDigest)" })
$result.interactionDataDigests = $interaction
if ($interaction.Count -ge 2 -and @($interaction | Select-Object -Unique).Count -gt 1) { $result.reasons += "interaction-data digest differs between the runs: $($interaction -join ' vs ')" }
# With -Against only the fresh build has a receipt, so there is nothing to compare its digests with.
if ($Against) { $result.digestComparison = "not available: the existing folder has no receipt, so only the fresh build's digests are listed" }
if (@($result.runs | Where-Object { $_.Contains("importClosureChecked") -and -not $_.importClosureChecked }).Count -gt 0) {
    $result.reasons += "the build did not report its imported modules (no 'BUILD_MC39 MODULES' line), so the hashed input list is not proven complete"
}
if ($result.reasons.Count -eq 0 -and -not $controlDir -and -not $Against) {
    $result.verdict = "INCOMPLETE"; $result.reasons += "the two runs match, but no negative control was run (-ControlSet `"<key>=<value>`")"; Finish 2
}
if ($result.reasons.Count -eq 0) { $result.verdict = "PASS"; Finish 0 }
Finish 1
