# Issue #9 frustum-culling measurement matrix (task #26), Windows release player only.
# Runs exactly the matrix of TestResults/9-measurement-plan.md (revision 2, git hash-object 710f0d11):
#   spot block   15 launches: 5 spots x 3 runs, one spot per launch, windows A V1 A .. Vk A inside the player (FrustumProbe)
#   route census  1 launch:  A only at the route waypoints outdoor_centre and tower_base (counts, timings unused)
#   route block  13 launches: -bbFpsWalk, one variant per launch: A FAR A LAYER A LAYER A FAR A FAR A LAYER A
#   optional     -DevCounters: the development-build counter pass (5 launches, counters only, never timed)
#   first        -PoseCheck: one short stills-only launch at all five spots (about 3 min), checked by platform-tools-engineer
#                and technical-artist before the matrix (pose rule N3, #25 note_technical-director_plan_n3); not timed
# The trial settings are runtime flags read by Assets/Scripts/Perf/FrustumProbe.cs; nothing is saved to the project.
#
# LAUNCHES A VISIBLE GAME WINDOW (right-hand monitor, -monitor 1). Only the Studio Head runs this (decision 0016).
#
# Usage (from the repo root, PowerShell 7 or Windows PowerShell 5.1):
#   pwsh tools/perf/frustum_matrix.ps1 -DryRun                 every launch, its arguments and the guards; launches nothing
#   pwsh tools/perf/frustum_matrix.ps1 -PoseCheck              step 1: the short pose-check launch (stills at each spot)
#   pwsh tools/perf/frustum_matrix.ps1 -Slots park,ridge_top,valley_floor,cab,fish_water
#                                                              plan section 5's INCONCLUSIVE rerun (Studio Head, 9-studio-head-rulings.md
#                                                              item 3): only the spot launches of those spots, unchanged args, run id -rerun
#   pwsh tools/perf/frustum_matrix.ps1 -RouteOnly              route set 2 (technical-director's addendum 6): only the 13
#                                                              route slots, unchanged args, under a new run id (-route2)
#   pwsh tools/perf/frustum_matrix.ps1 -FishSample             the fish sample (addendum 5): one untimed launch, a 60 s turn
#                                                              at fish_c1 then fish_c2, fish counted every frame
# Fish (addendum 5, #25 note_technical-director_fish_sample): the matrix reads the newest finished -FishSample run (or
# -FishSampleRun <id>) and refuses without one, or when that sample ran on another build (its run.json buildSha256). The first of fish_c1, fish_c2 whose window shows a fish on >= 10% of
# frames becomes fish_water (A FISHOFF A x 3 launches); if neither does, there are no fish_water launches.
#   pwsh tools/perf/frustum_matrix.ps1                         the whole matrix (about 2 h 45 min, unattended)
#   pwsh tools/perf/frustum_matrix.ps1 -RunId <id>             resume a stopped session: slots with a DONE are skipped
#   pwsh tools/perf/frustum_matrix.ps1 -DevCounters            the optional counter pass (about 25 min), after the matrix
#   pwsh tools/perf/frustum_matrix.ps1 -Summary -RunId <id>    summary.json from what is on disk; launches nothing
#   pwsh tools/perf/frustum_matrix.ps1 -DryRun -SimulateInvalid s03-r1-valley_floor[,s03-r1-valley_floor-repeat]
#                                                              the repeat rule on a pretend invalid window; launches nothing
# Repeat rule (technical-director, #26 note_technical-director_departure; plan section 4): after each spot launch its
# frustum-windows.jsonl must exist, hold 2k+1 rows (k = its variants) and every row valid:true. If not, one repeat of
# that spot and run index is appended at the end of the spot block and replaces the whole launch. At most one per spot
# and run; a repeat is never repeated.
# Results: TestResults/9-runs/<run-id>/<slot>/ (Player.log, progress.txt, JSON, stills, run.json),
#          TestResults/9-runs/<run-id>/matrix-progress.txt (one line per slot; last line "MATRIX DONE ...") and summary.json.
# Exit codes: 0 every slot ok; 1 a slot failed after its retry; 2 refused to start.
param(
    [string]$RunId = "",
    [switch]$DryRun,
    [switch]$DevCounters,
    [switch]$PoseCheck,
    [switch]$FishSample,
    [switch]$RouteOnly,
    [Alias("Slots")][string]$RerunSpots = "",   # -Slots: PowerShell names are case-insensitive, so it can't share the name $slots
    [string]$FishSampleRun = "",
    [string]$SimulateFishWinner = "",
    [switch]$Summary,
    [string]$SimulateInvalid = "",
    [string]$Exe = "Builds/Windows-BellsBend-frustum9/WashedAshorePOC.exe",
    [string]$DevExe = "Builds/Windows-BellsBend-frustum9-dev/WashedAshorePOC.exe",
    [string]$Plan = "TestResults/9-measurement-plan.md",
    [string]$PlanBlob = "710f0d11b3dea7fa610a4874b186eff334e0050f",
    [string]$TowerParams = "tools/blender/lookout/mc39_params.json",
    [int]$TimeoutSec = 1500
)
$ErrorActionPreference = "Stop"
# The repo root from this file's location, resolved in the body (in Windows PowerShell 5.1 $PSScriptRoot is empty inside a param() default; 0016).
$repo = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\..")).Path.TrimEnd('\')
Set-Location $repo
function FullPath([string]$p) { if ([System.IO.Path]::IsPathRooted($p)) { return $p } return (Join-Path $repo $p) }
$inv = [System.Globalization.CultureInfo]::InvariantCulture

# --- the plan ------------------------------------------------------------------------------------------------------
$planPath = FullPath $Plan
if (-not (Test-Path -LiteralPath $planPath)) { Write-Host "FRUSTUM REFUSED: plan not found: $planPath"; exit 2 }
$planBlobNow = (& git hash-object -- $planPath).Trim()
if ($planBlobNow -ne $PlanBlob) { Write-Host "FRUSTUM REFUSED: the plan changed ($planBlobNow, expected $PlanBlob). The matrix below is written for plan revision 2; a new plan needs a new script."; exit 2 }

# Cab eye height: cab-floor height from the tower's parameter table + 1.65 m, as farview_matrix.ps1 computes it (plan section 1).
$paramsPath = FullPath $TowerParams
if (-not (Test-Path -LiteralPath $paramsPath)) { Write-Host "FRUSTUM REFUSED: tower parameter table not found: $paramsPath"; exit 2 }
$table = Get-Content -LiteralPath $paramsPath -Raw | ConvertFrom-Json
$feet = $table.standards.($table.height_standard).height_ft
if (-not $feet) { Write-Host "FRUSTUM REFUSED: no height_ft for standard '$($table.height_standard)' in $TowerParams"; exit 2 }
$cabHeight = ([double]$feet * 0.3048 + 1.65).ToString("0.###", $inv)
$cabSource = "standards[$($table.height_standard)].height_ft $feet ft x 0.3048 + 1.65 m, $TowerParams git $((& git hash-object -- $paramsPath).Trim().Substring(0, 8))"

# Variants (plan section 4). FISHOFF runs only at fish_water (technical-director's fish addendum, #25
# note_technical-director_plan_fish_addendum): not at river_bank or valley_floor.
$V = [ordered]@{
    FAR       = "FAR:far=600,farCab=1150"
    LAYER     = "LAYER:cull.Wildlife=400,cull.Birds=400"
    SMALLMESH = "SMALLMESH:smallMesh=1"
    GRDOCC    = "GRDOCC:grdOcc=on"
    SHADOW    = "SHADOW:shadows=off"
    FISHOFF   = "FISHOFF:fishOff=on"
}
$order = @{
    1 = @("FAR", "LAYER", "SMALLMESH", "GRDOCC", "SHADOW")
    2 = @("SHADOW", "GRDOCC", "SMALLMESH", "LAYER", "FAR")
    3 = @("SMALLMESH", "SHADOW", "FAR", "GRDOCC", "LAYER")
}
$spotOrder = @{
    1 = @("park", "river_bank", "valley_floor", "ridge_top", "cab")
    2 = @("cab", "ridge_top", "valley_floor", "river_bank", "park")
    3 = @("valley_floor", "cab", "park", "ridge_top", "river_bank")
}
# fish_water: 3 launches of A FISHOFF A (runs 1-3) after the spot block's last regular slot, before any repeat, at the
# fish-sample winner's pose (passed with -fcPose fish_water=...). Decided below from the fish sample; empty = none.
$FishWaterSpot = ""; $FishWaterPose = ""; $FishDecision = ""; $FishShareMin = 0.10
$shoreCand = @((Get-Content -LiteralPath (FullPath "TestResults/9-shore-candidates.json") -Raw | ConvertFrom-Json).candidates)
$fishCands = @($shoreCand | Where-Object { $_.name -in "fish_c1", "fish_c2" } | Sort-Object order)
$common = "-fcWarmup 15 -fcSettle 5 -fcYawSpeed 15 -fcCabHeight $cabHeight"

# Build identity (technical-director, #26 review_technical-director_pose): sha256 over the ordinal-sorted list of
# "<relative path>\t<sha256>" lines for EVERY file in the player's folder, recursively: the exe, UnityPlayer.dll,
# MonoBleedingEdge/, <exe>_Data/ and anything else Unity put there (the runtime is part of what is measured). The exe
# alone is Unity's launcher stub and doesn't change with the code. Plan test 1's "same exe hash" means this build hash.
# The WashedAshore.Perf.dll hash is kept as information only. A file a run writes inside the folder is excluded by its
# exact relative path in $BuildHashExclude and listed in run.json; the runner also hashes the folder after each launch
# and records any file the launch added, removed or changed.
$BuildHashExclude = @()
function BuildFiles([string]$exe) {
    $exePath = FullPath $exe
    $dir = Split-Path -Parent $exePath
    $map = @{}
    foreach ($f in @(Get-ChildItem -LiteralPath $dir -Recurse -File)) {
        $rel = $f.FullName.Substring($dir.Length + 1).Replace('\', '/')
        if ($BuildHashExclude -contains $rel) { continue }
        $map[$rel] = (Get-FileHash -Algorithm SHA256 -LiteralPath $f.FullName).Hash.ToLowerInvariant()
    }
    return $map
}
function BuildHash([string]$exe) {
    $exePath = FullPath $exe
    $dir = Split-Path -Parent $exePath
    $data = Join-Path $dir ([System.IO.Path]::GetFileNameWithoutExtension($exePath) + "_Data")
    $map = BuildFiles $exe
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($k in $map.Keys) { $lines.Add($k + "`t" + $map[$k]) }
    $sorted = [string[]]$lines.ToArray()
    [System.Array]::Sort($sorted, [System.StringComparer]::Ordinal)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($sorted -join "`n") + "`n")
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hash = -join ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString("x2") })
    $perf = Join-Path $data "Managed/WashedAshore.Perf.dll"
    $perfSha = if (Test-Path -LiteralPath $perf) { (Get-FileHash -Algorithm SHA256 -LiteralPath $perf).Hash.ToLowerInvariant() } else { "" }
    return [pscustomobject]@{ hash = $hash; files = $sorted.Count; perf_dll = $perfSha; map = $map }
}
# Files a launch added, removed or changed inside the player folder (should be none).
function BuildDelta($before, $after) {
    $d = @()
    foreach ($k in $after.Keys) { if (-not $before.ContainsKey($k)) { $d += "added $k" } elseif ($before[$k] -ne $after[$k]) { $d += "changed $k" } }
    foreach ($k in $before.Keys) { if (-not $after.ContainsKey($k)) { $d += "removed $k" } }
    return ,$d
}

$slots = New-Object System.Collections.Generic.List[object]
function AddSlot([string]$id, [string]$block, [string]$argLine, [string]$expect, [string]$exe) {
    $slots.Add([pscustomobject]@{ id = $id; block = $block; args = $argLine; expect = $expect; exe = $exe; repeat = $false; of = "" })
}
if ($FishSample) {
    # One untimed launch: a 3 s warm-up, a 2 s settle, then one 360 degree turn at 6 deg/s (60 s) at each candidate, so
    # the share covers every heading as the matrix's turning A windows do. fish_c3 is out (eye 27.6 m above the water,
    # past the 25 m fish draw radius).
    $poses = ($fishCands | ForEach-Object { "-fcPose $($_.fcPose)" }) -join " "
    AddSlot "f01-fishsample" "fishsample" "-fcSpots fish_c1,fish_c2 $poses -fcRun 1 -fcArm fishsample -fcWarmup 3 -fcSettle 2 -fcYawSpeed 6 -fcNoShots -fcCabHeight $cabHeight" "DONE spots" $Exe
} elseif ($PoseCheck) {
    # One A window per spot with a fast sweep: stills at 0/90/180/270 and the census, nothing timed that counts.
    # Candidates fixed before the check (td's pose addenda and fish addendum, #25): ridge_top in
    # TestResults/9-ridge-candidates.json (RidgeCandidates.cs), river_bank and fish_water in TestResults/9-shore-candidates.json
    # (ShoreCandidates.cs). Each runs beside the current poses under its own -fcPose name, in its file's order.
    $candSpots = ""; $candPoses = ""
    foreach ($file in "TestResults/9-ridge-candidates.json", "TestResults/9-shore-candidates.json") {
        $candPath = FullPath $file
        if (-not (Test-Path -LiteralPath $candPath)) { Write-Host "FRUSTUM REFUSED: candidate list missing: $file"; exit 2 }
        $all = @((Get-Content -LiteralPath $candPath -Raw | ConvertFrom-Json).candidates | Where-Object { $_.order -ge 1 })
        foreach ($c in @($all | Sort-Object { $_.name -replace '\d+$', '' }, order)) { $candSpots += ",$($c.name)"; $candPoses += " -fcPose $($c.fcPose)" }
    }
    AddSlot "p01-posecheck" "posecheck" "-fcSpots park,river_bank,valley_floor,ridge_top,cab$candSpots$candPoses -fcRun 1 -fcArm posecheck -fcWarmup 3 -fcSettle 1 -fcYawSpeed 90 -fcCabHeight $cabHeight" "DONE spots" $Exe
} elseif (-not $DevCounters) {
    # The fish decision (addendum 5), from the fish sample's per-frame share.
    if ($SimulateFishWinner -and -not $DryRun) { Write-Host "FRUSTUM REFUSED: -SimulateFishWinner only works with -DryRun"; exit 2 }
    $sampleDir = $null
    if ($FishSampleRun) { $sampleDir = Join-Path (FullPath "TestResults/9-runs") "$FishSampleRun/f01-fishsample" }
    else {
        $found = @(Get-ChildItem -LiteralPath (FullPath "TestResults/9-runs") -Directory -Filter "*-fishsample" -ErrorAction SilentlyContinue |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "f01-fishsample/frustum-spots.json") } | Sort-Object Name)
        if ($found.Count -gt 0) { $sampleDir = Join-Path $found[-1].FullName "f01-fishsample" }
    }
    if ($SimulateFishWinner) {
        $w = $fishCands | Where-Object { $_.name -eq $SimulateFishWinner } | Select-Object -First 1
        if ($w) { $FishWaterSpot = "fish_water"; $FishWaterPose = $w.fcPose -replace '^[^=]+=', ''; $FishDecision = "SIMULATED winner $SimulateFishWinner (dry run only)" }
        else { $FishDecision = "SIMULATED: no winner" }
    } elseif (-not $sampleDir -or -not (Test-Path -LiteralPath (Join-Path $sampleDir "frustum-spots.json"))) {
        $FishDecision = "NO FISH SAMPLE: run -FishSample first"
    } else {
        # Technical-director's ruling (#26): a fish sample counts only if it ran on the build this session runs on.
        $sampleRun = Join-Path $sampleDir "run.json"
        $sampleBuild = if (Test-Path -LiteralPath $sampleRun) { (Get-Content -LiteralPath $sampleRun -Raw | ConvertFrom-Json).buildSha256 } else { "" }
        $nowBuild = if (Test-Path -LiteralPath (FullPath $Exe)) { (BuildHash $Exe).hash } else { "" }
        $fs = Get-Content -LiteralPath (Join-Path $sampleDir "frustum-spots.json") -Raw | ConvertFrom-Json
        $parts = @()
        foreach ($c in $fishCands) {
            $win = @($fs.windows | Where-Object { $_.spot -eq $c.name }) | Select-Object -First 1
            $share = if ($win -and $win.fish_sample) { [double]$win.fish_sample.share_at_least_1 } else { -1 }
            $parts += "$($c.name) share $share (frames $($win.fish_sample.frames), max $($win.fish_sample.max), valid $($win.valid))"
            if (-not $FishWaterSpot -and $win -and $win.valid -and $share -ge $FishShareMin) { $FishWaterSpot = "fish_water"; $FishWaterPose = $c.fcPose -replace '^[^=]+=', ''; $parts[-1] += " -> WINNER" }
        }
        $FishDecision = "fish sample $sampleDir`: " + ($parts -join "; ") + $(if ($FishWaterSpot) { "" } else { "; no candidate >= ${FishShareMin}: no fish_water launches" })
        if (-not $sampleBuild -or $sampleBuild -ne $nowBuild) {
            $FishWaterSpot = ""; $FishWaterPose = ""
            $FishDecision = "FISH SAMPLE FROM ANOTHER BUILD: $sampleDir ran on build '$sampleBuild', this session's build is '$nowBuild'; run -FishSample on this build"
        }
    }
    $n = 0
    foreach ($r in 1, 2, 3) {
        foreach ($spot in $spotOrder[$r]) {
            $n++
            $vs = @($order[$r])
            $vargs = ($vs | ForEach-Object { "-fcVariant $($V[$_])" }) -join " "
            AddSlot ("s{0:00}-r{1}-{2}" -f $n, $r, $spot) "spots" "-fcSpots $spot -fcRun $r -fcArm r$r-$spot $common -fcEdgeShots $vargs" "DONE spots" $Exe
        }
    }
    if ($FishWaterSpot) {
        foreach ($r in 1, 2, 3) {
            $n++
            AddSlot ("s{0:00}-r{1}-{2}" -f $n, $r, $FishWaterSpot) "spots" "-fcSpots $FishWaterSpot -fcPose $FishWaterSpot=$FishWaterPose -fcRun $r -fcArm r$r-$FishWaterSpot $common -fcVariant $($V['FISHOFF'])" "DONE spots" $Exe
        }
    }
    AddSlot "c01-route-census" "census" "-fcSpots outdoor_centre,tower_base -fcPose outdoor_centre=-145.88,-852.69,48.03,0 -fcPose tower_base=544.02,-232.17,228.03,0 -fcRun 1 -fcArm route-census $common" "DONE spots" $Exe
    $route = @("A", "FAR", "A", "LAYER", "A", "LAYER", "A", "FAR", "A", "FAR", "A", "LAYER", "A")
    $routeFlags = @{ A = ""; FAR = "-fcFar 600"; LAYER = "-fcLayerCull Wildlife=400 -fcLayerCull Birds=400" }
    for ($i = 0; $i -lt $route.Count; $i++) {
        $v = $route[$i]
        AddSlot ("w{0:00}-{1}" -f ($i + 1), $v) "route" ("-bbFpsWalk -fcArm route-{0:00}-{1} -fcRun {2} {3}" -f ($i + 1), $v, ($i + 1), $routeFlags[$v]).Trim() "DONE route" $Exe
    }
} else {
    # Optional counter pass (plan section 4, "Optional, last"): development build, counters only, frame times never used.
    $n = 0
    foreach ($spot in $spotOrder[1]) {
        $n++
        $vargs = (@("FAR", "LAYER", "SMALLMESH") | ForEach-Object { "-fcVariant $($V[$_])" }) -join " "
        AddSlot ("d{0:00}-dev-{1}" -f $n, $spot) "dev" "-fcSpots $spot -fcRun 1 -fcArm dev-$spot $common -fcNoShots $vargs" "DONE spots" $DevExe
    }
}

# Route set 2 (addendum 6): keep only the route slots of the matrix, with their arguments unchanged; no fish decision needed.
if ($RouteOnly) {
    if ($PoseCheck -or $FishSample -or $DevCounters) { Write-Host "FRUSTUM REFUSED: -RouteOnly goes alone"; exit 2 }
    $routeSlots = @($slots | Where-Object { $_.block -eq "route" })
    $slots.Clear(); foreach ($x in $routeSlots) { $slots.Add($x) }
    $FishDecision = ""
}
# INCONCLUSIVE rerun: keep only the spot launches of the named spots, in their original order and with unchanged args.
if ($RerunSpots) {
    if ($PoseCheck -or $FishSample -or $DevCounters -or $RouteOnly) { Write-Host "FRUSTUM REFUSED: -Slots goes alone"; exit 2 }
    $want = @($RerunSpots -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $keep = @($slots | Where-Object { $_.block -eq "spots" -and ($want -contains ($_.id -replace '^s\d\d-r\d-', '')) })
    $found = @($keep | ForEach-Object { $_.id -replace '^s\d\d-r\d-', '' } | Select-Object -Unique)
    $missing = @($want | Where-Object { $found -notcontains $_ })
    if ($missing.Count -gt 0) { Write-Host "FRUSTUM REFUSED: -Slots names no spot launch for: $($missing -join ', ')"; exit 2 }
    $slots.Clear(); foreach ($x in $keep) { $slots.Add($x) }
}
if (-not $RunId) { $RunId = (Get-Date).ToString("yyyyMMdd-HHmm") + $(if ($DevCounters) { "-dev" } elseif ($PoseCheck) { "-posecheck" } elseif ($FishSample) { "-fishsample" } elseif ($RouteOnly) { "-route2" } elseif ($RerunSpots) { "-rerun" } else { "" }) }
$root = Join-Path (FullPath "TestResults/9-runs") $RunId
$matrixLog = Join-Path $root "matrix-progress.txt"

function SlotDir($s) { return (Join-Path $root $s.id) }
function SlotDone($s) {
    $p = Join-Path (SlotDir $s) "progress.txt"
    if (-not (Test-Path -LiteralPath $p)) { return $false }
    $last = @(Get-Content -LiteralPath $p | Where-Object { $_.Trim() })[-1]
    return ($last -match ("\s" + [regex]::Escape($s.expect) + "$"))
}

# --- summary (reads only finished files) ---------------------------------------------------------------------------
function WriteSummary() {
    $rows = @()
    # Repeats are added during a session; a later -Summary finds them by their folders.
    # A typed list copied element by element: @() round a generic List throws "Argument types do not match" in pwsh 7.
    $all = New-Object System.Collections.Generic.List[object]
    for ($j = 0; $j -lt $slots.Count; $j++) { $all.Add($slots[$j]) }
    for ($j = 0; $j -lt $slots.Count; $j++) {
        $x = $slots[$j]
        $rid = "$($x.id)-repeat"
        $known = $false
        foreach ($y in $all) { if ($y.id -eq $rid) { $known = $true } }
        if ($x.block -eq "spots" -and -not $x.repeat -and -not $known -and (Test-Path -LiteralPath (Join-Path $root $rid))) {
            $all.Add([pscustomobject]@{ id = $rid; block = "spots"; args = $x.args; expect = $x.expect; exe = $x.exe; repeat = $true; of = $x.id })
        }
    }
    foreach ($s in $all) {
        $dir = SlotDir $s
        $row = [ordered]@{ slot = $s.id; block = $s.block; done = (SlotDone $s); repeat_of = $s.of; args = $s.args }
        $run = Join-Path $dir "run.json"
        if (Test-Path -LiteralPath $run) { $rj = Get-Content -LiteralPath $run -Raw | ConvertFrom-Json; $row.ok = $rj.ok; $row.problems = $rj.problems; $row.exe_sha256 = $rj.exeSha256; $row.build_sha256 = $rj.buildSha256; $row.seconds = $rj.seconds }
        $spots = Join-Path $dir "frustum-spots.json"
        $routeJson = Join-Path $dir "frustum-route.json"
        if (Test-Path -LiteralPath $spots) {
            $j = Get-Content -LiteralPath $spots -Raw | ConvertFrom-Json
            $row.player_exe_sha256 = $j.exe_sha256; $row.resolution = $j.resolution; $row.argument_errors = @($j.argument_errors)
            $row.windows = @($j.windows | ForEach-Object { [ordered]@{ spot = $_.spot; window = $_.window; variant = $_.variant_id; valid = $_.valid; problems = @($_.problems)
                samples = $_.frame_ms.count; median_ms = $_.frame_ms.median; p95_ms = $_.frame_ms.p95; p99_ms = $_.frame_ms.p99
                triangles_median = $_.counters.triangles.median; batches_median = $_.counters.batches.median
                visible_skinned_median = $_.counters.visible_skinned_meshes.median; shadow_casters_median = $_.counters.shadow_casters.median
                still_px = @($_.headings | ForEach-Object { if ($_.still -and $_.still.vs_previous_A) { $_.still.vs_previous_A.total_px } }) | Select-Object -First 1 } })
        } elseif (Test-Path -LiteralPath $routeJson) {
            $j = Get-Content -LiteralPath $routeJson -Raw | ConvertFrom-Json
            $row.player_exe_sha256 = $j.exe_sha256; $row.valid = $j.valid; $row.problems_player = @($j.problems)
            $row.samples = $j.frame_ms.count; $row.median_ms = $j.frame_ms.median; $row.p95_ms = $j.frame_ms.p95; $row.p99_ms = $j.frame_ms.p99
            $row.triangles_median = $j.counters.triangles.median; $row.visible_skinned_median = $j.counters.visible_skinned_meshes.median
        }
        $rows += $row
    }
    # Route: an arm more than 5% below the block's most samples is void (qa-lead's rule, farview_matrix.ps1).
    $g = @($rows | Where-Object { $_.block -eq "route" -and $_.samples })
    if ($g.Count -ge 2) {
        # Rows are ordered dictionaries, which Measure-Object -Property can't read (it gave 0), so the maximum is taken by hand.
        $most = 0
        foreach ($r in $g) { if ([double]$r.samples -gt $most) { $most = [double]$r.samples } }
        foreach ($r in $g) { $r.samples_vs_block_max = [math]::Round($r.samples / $most, 3); $r.void_by_sample_count = ($r.samples -lt 0.95 * $most) }
    }
    $shas = @($rows | Where-Object { $_.player_exe_sha256 } | ForEach-Object { $_.player_exe_sha256 } | Select-Object -Unique)
    $builds = @($rows | Where-Object { $_.build_sha256 } | ForEach-Object { $_.build_sha256 } | Select-Object -Unique)
    $out = [ordered]@{ run_id = $RunId; plan = $Plan; plan_blob = $planBlobNow; cab_height = $cabHeight; cab_height_source = $cabSource
        build_sha256_distinct = $builds; same_build = ($builds.Count -le 1); exe_sha256_distinct = $shas; same_exe = ($shas.Count -le 1); slots = $rows }
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $root "summary.json"), (ConvertTo-Json -InputObject $out -Depth 8))
    $doneCount = @($rows | Where-Object { $_.done }).Count
    Write-Host "FRUSTUM summary=$(Join-Path $root 'summary.json') slots_done=$doneCount/$($all.Count) same_build=$($builds.Count -le 1) same_exe=$($shas.Count -le 1)"
}
if ($Summary) { if (-not (Test-Path -LiteralPath $root)) { Write-Host "FRUSTUM REFUSED: no such run: $root"; exit 2 }; WriteSummary; exit 0 }

# --- machine guards (from farview_matrix.ps1: heavy processes, the Unity CLI helper bar, console session) -------------
$HelperPattern = '(^|\s)--internal-auth-broker-serve(\s|$)'; $EditorPattern = '(^|\s)-(projectpath|batchmode|executemethod)(\s|$)'; $HelperCpuBar = 0.2
function UnitySplit() {
    $ids = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object { [int]$_.Id })
    $lines = @{}
    if ($ids.Count -gt 0) { foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue)) { $lines[[int]$p.ProcessId] = [string]$p.CommandLine } }
    $editors = @(); $helpers = @()
    foreach ($id in $ids) {
        $cl = if ($lines.ContainsKey($id)) { [string]$lines[$id] } else { "" }
        if ($cl -match $HelperPattern -and $cl -notmatch $EditorPattern) { $helpers += $id } else { $editors += $id }
    }
    return @{ editors = $editors; helpers = $helpers }
}
function Heavy() {
    $split = UnitySplit
    $list = @(Get-Process blender, ffmpeg -ErrorAction SilentlyContinue | ForEach-Object { "$($_.ProcessName):$($_.Id)" })
    foreach ($id in $split.editors) { $list += "Unity:$id" }
    return ,$list
}
function HelperCpu() {
    $split = UnitySplit
    $rows = @()
    if ($split.helpers.Count -eq 0) { return ,$rows }
    $t = @{}; $c = [System.Diagnostics.Stopwatch]::StartNew()
    foreach ($id in $split.helpers) { $q = Get-Process -Id $id -ErrorAction SilentlyContinue; if ($q) { $t[$id] = $q.TotalProcessorTime.TotalSeconds } }
    Start-Sleep -Milliseconds 1000
    foreach ($id in $split.helpers) {
        $q = Get-Process -Id $id -ErrorAction SilentlyContinue
        $share = if ($q -and $t.ContainsKey($id)) { [math]::Round(($q.TotalProcessorTime.TotalSeconds - $t[$id]) / $c.Elapsed.TotalSeconds, 3) } else { $null }
        $rows += [ordered]@{ id = $id; cpu_s_per_s = $share }
    }
    return ,$rows
}
function ConsoleActive() { return ((query user 2>$null | Out-String) -match "(?m)^>.*\bconsole\b.*\bActive\b") }
function Load() {
    $cpu = [math]::Round((Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 1 -MaxSamples 2).CounterSamples[-1].CookedValue, 1)
    return [ordered]@{ time = (Get-Date).ToString("o"); cpu_total_percent = $cpu; render_or_editor_processes = (Heavy); unity_cli_helpers = (HelperCpu) }
}

$exes = @($slots | ForEach-Object { $_.exe } | Select-Object -Unique)
$lockPath = FullPath ".studio/unity.lock"
$refuse = @()
foreach ($e in $exes) { if (-not (Test-Path -LiteralPath (FullPath $e))) { $refuse += "player not built: $e" } }
if (Get-Process WashedAshorePOC -ErrorAction SilentlyContinue) { $refuse += "WashedAshorePOC is already running" }
$heavyNow = Heavy
if ($heavyNow.Count -gt 0) { $refuse += "heavy process running (close the Unity Editor, Blender, ffmpeg): $($heavyNow -join ', ')" }
if (-not (ConsoleActive)) { $refuse += "the console session is not active (run at the machine, not over RDP)" }
if ($FishDecision -like "NO FISH SAMPLE*" -or $FishDecision -like "FISH SAMPLE FROM ANOTHER BUILD*") { $refuse += $FishDecision }
if (Test-Path -LiteralPath $lockPath) { $refuse += ".studio/unity.lock is held: $((Get-Content -LiteralPath $lockPath -Raw).Trim())" }

$exeSha = @{}
foreach ($e in $exes) { if (Test-Path -LiteralPath (FullPath $e)) { $exeSha[$e] = (Get-FileHash -Algorithm SHA256 -LiteralPath (FullPath $e)).Hash.ToLowerInvariant() } }
# The session's build: the first launch's hash, or, on a resume, the one recorded by the first finished launch.
$sessionBuild = $null

# --- the repeat rule ------------------------------------------------------------------------------------------------
# Why a finished spot launch needs its repeat, or $null. Read only after the player has exited.
function JsonlProblem($s) {
    $f = Join-Path (SlotDir $s) "frustum-windows.jsonl"
    if (-not (Test-Path -LiteralPath $f)) { return "no frustum-windows.jsonl" }
    $k = ([regex]::Matches($s.args, "-fcVariant ")).Count
    $rows = @(Get-Content -LiteralPath $f | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
    if ($rows.Count -lt 2 * $k + 1) { return "$($rows.Count) window rows, expected $(2 * $k + 1)" }
    $bad = @($rows | Where-Object { $_.valid -ne $true })
    if ($bad.Count -gt 0) { return "invalid windows: " + (($bad | ForEach-Object { "w$($_.window) $($_.variant_id) ($(@($_.problems) -join ', '))" }) -join "; ") }
    return $null
}
# Appends the one repeat of a spot launch after the last slot of the spot block; returns what it did.
function AddRepeat($s) {
    if ($s.repeat) { return "REPEAT NOT ADDED for $($s.id): it is itself a repeat (never repeat a repeat)" }
    $rid = "$($s.id)-repeat"
    foreach ($x in $slots) { if ($x.id -eq $rid) { return "REPEAT already queued: $rid" } }
    $last = -1
    for ($j = 0; $j -lt $slots.Count; $j++) { if ($slots[$j].block -eq "spots") { $last = $j } }
    $slots.Insert($last + 1, [pscustomobject]@{ id = $rid; block = "spots"; args = $s.args; expect = $s.expect; exe = $s.exe; repeat = $true; of = $s.id })
    return "REPEAT QUEUED $rid (replaces $($s.id)) at position $($last + 2)"
}

if ($SimulateInvalid -and -not $DryRun) { Write-Host "FRUSTUM REFUSED: -SimulateInvalid only works with -DryRun"; exit 2 }
if ($DryRun) {
    # The same scheduling as the session, with the jsonl check answered by -SimulateInvalid instead of a launch.
    $pretend = @($SimulateInvalid -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $events = @()
    for ($i = 0; $i -lt $slots.Count; $i++) {
        $s = $slots[$i]
        if ($s.block -ne "spots") { continue }
        if ($pretend -contains $s.id) { $events += "simulated invalid row in $($s.id) -> " + (AddRepeat $s) }
    }
    Write-Host "FRUSTUM DRY RUN (nothing is launched). Plan $Plan blob $planBlobNow. Cab height $cabHeight m ($cabSource)."
    Write-Host "Run id: $RunId -> $root"
    if ($FishDecision) { Write-Host "Fish decision: $FishDecision" }
    foreach ($e in $exes) {
        $bh = if (Test-Path -LiteralPath (FullPath $e)) { BuildHash $e } else { $null }
        Write-Host "Player: $e exe sha256 $($exeSha[$e]); build hash $(if ($bh) { "$($bh.hash) over $($bh.files) files; WashedAshore.Perf.dll $($bh.perf_dll)" } else { 'n/a' })"
    }
    foreach ($s in $slots) {
        $state = if (SlotDone $s) { "done (skipped)" } else { "to run" }
        Write-Host ("{0,-32} {1,-7} {2}" -f $s.id, $state, "$($s.exe) -monitor 1 $($s.args) -logFile <slot>/Player.log")
    }
    foreach ($e in $events) { Write-Host "Repeat rule: $e" }
    if ($pretend.Count -gt 0 -and $events.Count -eq 0) { Write-Host "Repeat rule: no simulated id matched a spot slot" }
    Write-Host "Slots: $($slots.Count). Would refuse now: $(if ($refuse.Count) { $refuse -join '; ' } else { 'no' })"
    exit 0
}
if ($refuse.Count -gt 0) { Write-Host "FRUSTUM REFUSED: $($refuse -join '; ')"; exit 2 }

# --- the session ---------------------------------------------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $root | Out-Null
[System.IO.File]::WriteAllText($lockPath, "Studio Head, tools/perf/frustum_matrix.ps1 run $RunId (#9 measurement), $((Get-Date).ToUniversalTime().ToString('o'))`n")
function Note([string]$line) { Add-Content -LiteralPath $matrixLog -Value ("{0} {1}" -f (Get-Date).ToUniversalTime().ToString("o"), $line) }
Note "MATRIX START run=$RunId plan_blob=$planBlobNow slots=$($slots.Count) cab_height=$cabHeight"
if ($FishDecision) { Note "FISH $FishDecision" }

function RunSlot($s, [string]$dir) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $log = Join-Path $dir "Player.log"
    $problems = @()
    if (Get-Process WashedAshorePOC -ErrorAction SilentlyContinue) { return @("WashedAshorePOC is already running") }
    if (-not (ConsoleActive)) { return @("the console session is not active") }
    $loadStart = Load
    if ($loadStart.render_or_editor_processes.Count -gt 0) { return @("heavy process running: $($loadStart.render_or_editor_processes -join ', ')") }
    # Pin the build before every launch: refuse when it differs from the session's first launch.
    $bh = BuildHash $s.exe
    if ($null -eq $script:sessionBuild) {
        $script:sessionBuild = $bh.hash
        foreach ($rjPath in @(Get-ChildItem -LiteralPath $root -Filter run.json -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)) {
            $prev = (Get-Content -LiteralPath $rjPath.FullName -Raw | ConvertFrom-Json).buildSha256
            if ($prev) { $script:sessionBuild = $prev; break }
        }
    }
    if ($bh.hash -ne $script:sessionBuild) { return @("BUILD CHANGED: build hash $($bh.hash) differs from the session's $($script:sessionBuild)") }
    $argLine = "-monitor 1 $($s.args) -logFile `"$log`""
    $started = Get-Date
    $proc = Start-Process -FilePath (FullPath $s.exe) -ArgumentList $argLine -PassThru
    $null = $proc.Handle
    $timedOut = -not $proc.WaitForExit($TimeoutSec * 1000)
    if ($timedOut) { $proc.Kill(); $proc.WaitForExit() }
    $finished = Get-Date
    $buildDelta = BuildDelta $bh.map (BuildFiles $s.exe)
    if ($buildDelta.Count -gt 0) { $problems += "the launch changed the player folder: $($buildDelta -join ', ')" }
    $loadEnd = Load
    # The player's own files, read only after it has exited (never open a file the runner is writing; 0016).
    $progress = Join-Path $dir "progress.txt"
    $last = if (Test-Path -LiteralPath $progress) { @(Get-Content -LiteralPath $progress | Where-Object { $_.Trim() })[-1] } else { "" }
    $sessions = if (Test-Path -LiteralPath $log) { @(Select-String -LiteralPath $log -Pattern "Initialize engine version").Count } else { 0 }
    $refused = if (Test-Path -LiteralPath $log) { @(Select-String -LiteralPath $log -Pattern "\[Frustum\] REFUSED").Count } else { 0 }
    if ($timedOut) { $problems += "timed out after $TimeoutSec s" }
    if ($sessions -ne 1) { $problems += "$sessions sessions in Player.log (expected 1)" }
    if ($refused -gt 0) { $problems += "the probe refused to run (see Player.log)" }
    if ($last -notmatch ("\s" + [regex]::Escape($s.expect) + "$")) { $problems += "progress.txt does not end with '$($s.expect)' (last: '$last')" }
    $json = if ($s.block -eq "route") { Join-Path $dir "frustum-route.json" } else { Join-Path $dir "frustum-spots.json" }
    if (-not (Test-Path -LiteralPath $json)) { $problems += "no $(Split-Path -Leaf $json)" }
    elseif ((Get-Content -LiteralPath $json -Raw | ConvertFrom-Json).exe_sha256 -ne $exeSha[$s.exe]) { $problems += "the player read a different exe hash than the runner" }
    if ($s.block -eq "route" -and -not (Test-Path -LiteralPath (Join-Path $dir "bells-bend-fps.json"))) { $problems += "no bells-bend-fps.json (the walk did not finish)" }
    foreach ($h in @($loadStart.unity_cli_helpers) + @($loadEnd.unity_cli_helpers)) { if ($h -and $null -ne $h.cpu_s_per_s -and $h.cpu_s_per_s -ge $HelperCpuBar) { $problems += "VOID: Unity CLI helper $($h.id) at $($h.cpu_s_per_s) CPU-s/s" } }
    $run = [ordered]@{ slot = $s.id; block = $s.block; args = $argLine; exe = $s.exe; exeSha256 = $exeSha[$s.exe]; buildSha256 = $bh.hash; buildFiles = $bh.files; buildHashExcluded = @($BuildHashExclude); buildChangedByLaunch = $buildDelta; perfDllSha256 = $bh.perf_dll; started = $started.ToString("o"); finished = $finished.ToString("o")
        seconds = [math]::Round(($finished - $started).TotalSeconds, 1); exitCode = $proc.ExitCode; timedOut = $timedOut; sessionsInLog = $sessions; progressLast = $last
        loadAtStart = $loadStart; loadAtEnd = $loadEnd; plan_blob = $planBlobNow; cab_height = $cabHeight; ok = ($problems.Count -eq 0); problems = $problems }
    [System.IO.File]::WriteAllText((Join-Path $dir "run.json"), ($run | ConvertTo-Json -Depth 6))
    return ,$problems
}

$failed = @()
$buildChanged = $false
try {
    for ($i = 0; $i -lt $slots.Count; $i++) {
        $s = $slots[$i]
        if (SlotDone $s) {
            Note "SLOT $($s.id) skipped (already DONE)"
            # On a resume the repeat rule is applied again to finished spot launches, so a needed repeat is never lost.
            if ($s.block -eq "spots") { $why = JsonlProblem $s; if ($why) { Note "SLOT $($s.id) windows: $why"; Note (AddRepeat $s) } }
            continue
        }
        $dir = SlotDir $s
        # A slot with a Player.log but no DONE is a broken earlier attempt: keep it beside, never overwrite (one session per log).
        if (Test-Path -LiteralPath (Join-Path $dir "Player.log")) { Rename-Item -LiteralPath $dir -NewName ("{0}-broken-{1}" -f $s.id, (Get-Date).ToString("HHmmss")) }
        Write-Host "FRUSTUM slot $($s.id) ..."
        $p = RunSlot $s $dir
        if (($p -join ";") -match "^BUILD CHANGED|;BUILD CHANGED") { Note "SLOT $($s.id) REFUSED: $($p -join '; ')"; $failed += $s.id; $buildChanged = $true; break }
        if ($p.Count -gt 0) {
            # Plan section 4: a failed launch is rerun at once in the same slot.
            Note "SLOT $($s.id) FAILED: $($p -join '; '); retrying once"
            Rename-Item -LiteralPath $dir -NewName ("{0}-failed-{1}" -f $s.id, (Get-Date).ToString("HHmmss"))
            $p = RunSlot $s $dir
            if ($p.Count -gt 0) { Note "SLOT $($s.id) FAILED AGAIN: $($p -join '; ')"; $failed += $s.id; continue }
            Note "SLOT $($s.id) DONE (retry)"
        } else { Note "SLOT $($s.id) DONE" }
        if ($s.block -eq "spots") {
            $why = JsonlProblem $s
            if ($why) { Note "SLOT $($s.id) windows: $why"; Note (AddRepeat $s) }
        }
    }
} finally {
    Remove-Item -LiteralPath $lockPath -ErrorAction SilentlyContinue
}
WriteSummary
if ($buildChanged) { Note "MATRIX STOPPED: the build changed during the session"; Write-Host "FRUSTUM STOPPED: the player build changed during the session; nothing after $($failed[-1]) ran"; exit 2 }
if ($failed.Count -gt 0) { Note "MATRIX DONE with failures: $($failed -join ', ')"; Write-Host "FRUSTUM FINISHED WITH FAILURES: $($failed -join ', ')"; exit 1 }
Note "MATRIX DONE all $($slots.Count) slots"
Write-Host "FRUSTUM MATRIX DONE run=$RunId slots=$($slots.Count) results=$root"
exit 0
