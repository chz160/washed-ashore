# Far-view measurement runs for the lookout tower (tower-td-note.md section 7), Windows player only.
# Each arm is one player session with its own Player.log; the view distances are set by command-line arguments read by
# Assets/Scripts/Perf/FarViewProbe.cs, never saved into World.unity. The player quits by itself at the end of a run.
#
# LAUNCHES A VISIBLE GAME WINDOW (right-hand monitor, -monitor 1). Run an arm only with team-lead's OK for that launch.
#
# Usage: pwsh tools/perf/farview_matrix.ps1 -List                 (arms and their arguments; launches nothing)
#        pwsh tools/perf/farview_matrix.ps1 -Arm ground-a1        (one arm; refuses if that arm already has a Player.log)
#        pwsh tools/perf/farview_matrix.ps1 -Summary              (table from the JSON already on disk; launches nothing)
#        pwsh tools/perf/farview_matrix.ps1 -ProcessCheck         (what would refuse a launch right now; launches nothing)
# Results: TestResults/tower-tools/farview/<arm>/ (Player.log, JSON, stills, run.json) and farview-table.json.
# Exit codes: 0 ok; 1 run failed (no JSON, crash, timeout, more than one session in the log); 2 refused to launch.
param(
    [string]$Arm = "",
    [switch]$List,
    [switch]$Summary,
    [switch]$ProcessCheck,
    [string]$Exe = "Builds/Windows-BellsBend/WashedAshorePOC.exe",
    [string]$ResultDir = "TestResults/tower-tools/farview",
    [string]$RidgeEyes = "",     # "x,z;x,z" in world metres: viewpoints for the ridge stills (default: the probe's zone rule)
    [string]$TowerParams = "tools/blender/lookout/mc39_params.json",
    [string]$SiteRecord = "Data/terrain/build/map_vectors.json",
    [string]$NearValues = "0.04,0.03",   # near planes compared with the authored one in the near2-stills arm
    [int]$TimeoutSec = 900
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path.TrimEnd('\')
function FullPath([string]$p) { if (-not [System.IO.Path]::IsPathRooted($p)) { $p = Join-Path $repo $p }; return [System.IO.Path]::GetFullPath($p) }
$Exe = FullPath $Exe; $ResultDir = FullPath $ResultDir

# Ground: the shore walk at far plane 1000 and 1200, run A-B-A so the A-A spread sits beside the A-B difference.
# Cab: far plane x tree distance, fog end at the far plane and fog start at a quarter of it.
$arms = [ordered]@{
    "ground-a1" = "-waterFpsWalk -fvFar 1000"
    "ground-b"  = "-waterFpsWalk -fvFar 1200"
    "ground-a2" = "-waterFpsWalk -fvFar 1000"
}
foreach ($far in 2500, 3000) { foreach ($trees in 450, 1200, 2500) { $arms["cab-f$far-t$trees"] = "-fvCab -fvFar $far -fvTrees $trees -fvFogToFar" } }
# Second set, as reduced after the no-fog-thinning decision: the ground walk again as a clean A-B-A, and one run from
# the first set's cab position (the probe's default summit, 546.15, -230.94) at the authored far plane and fog.
$arms["ground2-a"] = "-waterFpsWalk -fvFar 1000"
$arms["ground2-b"] = "-waterFpsWalk -fvFar 1200"
$arms["ground2-a2"] = "-waterFpsWalk -fvFar 1000"
$arms["cab2-authored"] = "-fvCab"
# Ridge stills (needs the probe with FarViewRidge in the player): 1 km ground stills of the summit at far 1200 and
# authored fog, each with its summit-tile-hidden twin and the ridge-line measure. They ride on a 5 second cab session,
# after its timed window. The tower height for the measure's window is the cab-floor height of the standard chosen in
# the tower's parameter table (never a literal here).
$inv = [System.Globalization.CultureInfo]::InvariantCulture
function Sha16([string]$path) { return (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant().Substring(0, 16) }
$towerHeight = ""; $towerHeightSource = ""
$paramsPath = FullPath $TowerParams
if (Test-Path -LiteralPath $paramsPath) {
    $table = Get-Content -LiteralPath $paramsPath -Raw | ConvertFrom-Json
    $feet = $table.standards.($table.height_standard).height_ft
    if ($feet) {
        $towerHeight = ([double]$feet * 0.3048).ToString("0.###", $inv)
        $towerHeightSource = "to cab floor: standards[$($table.height_standard)].height_ft in $TowerParams sha256 $(Sha16 $paramsPath)"
    }
}
# The tower centre is the site record, never typed here: the LookoutTower landmark's projected point plus the level
# designer's site offset, as written by tools/terrain/build_terrain.py into the terrain build's map_vectors.json.
$site = ""; $siteSource = ""
$sitePath = FullPath $SiteRecord
if (Test-Path -LiteralPath $sitePath) {
    $lm = (Get-Content -LiteralPath $sitePath -Raw | ConvertFrom-Json).landmarks | Where-Object { $_.id -eq "LookoutTower" } | Select-Object -First 1
    if ($lm -and $lm.site -and $null -ne $lm.x -and $null -ne $lm.z) {
        $site = (([double]$lm.x + [double]$lm.site.offsetX).ToString("0.###", $inv)) + " " + (([double]$lm.z + [double]$lm.site.offsetZ).ToString("0.###", $inv))
        $siteSource = "LookoutTower x,z + site.offsetX,Z in $SiteRecord sha256 $(Sha16 $sitePath)"
    }
}
$eyeArgs = ""
foreach ($pair in ($RidgeEyes -split ";" | Where-Object { $_.Trim() })) {
    $xz = $pair.Split(","); if ($xz.Count -ne 2) { Write-Host "FARVIEW REFUSED: -RidgeEyes must be 'x,z;x,z': '$pair'"; exit 2 }
    $eyeArgs += " -fvRidgeEye $($xz[0].Trim()) $($xz[1].Trim())"
}
$arms["ridge2-stills"] = "-fvCab -fvCabSeconds 5 -fvRidge -fvSummit $site -fvSummitSource `"$siteSource`" -fvTowerHeight $towerHeight -fvTowerHeightSource `"$towerHeightSource`"$eyeArgs"
# Near-plane regression stills (technical-director's proposal for the climb area): authored near plane against 0.04 and
# 0.03 m from the cab position (site record; eye at cab-floor height + 1.65 m) and from the tower's foot, authored fog
# and far plane. Also on a 5 second cab session that is not a measurement.
$cabEye = ""
if ($towerHeight) { $cabEye = ([double]::Parse($towerHeight, $inv) + 1.65).ToString("0.###", $inv) }
$arms["near2-stills"] = "-fvCab -fvCabSeconds 5 -fvNearStills $NearValues -fvSummit $site -fvSummitSource `"$siteSource`" -fvCabHeight $cabEye"
# The first cab run also takes the 1 km ground stills of the summit ridge (far 1200, authored fog and trees, two
# bearings) and their twins.
$arms["cab-f2500-t450"] += " -fvRidge"

# How to read the second set, carried in its table rows. The cab run uses the slot-2 player build, so its scene is the
# one the first set's cab arms ran on; the ground runs come later, on a later build.
$cabNote = "The second far-view set runs on the slot-2 player build: the pre-placement scene (forest under the cab position; no clearing, pad, track or tower). cab2-authored is at the authored far plane (1000 m) and fog (120-520 m) at the first set's cab position, and is comparable with the first set's clean cab arms, which differed only in far plane and fog. It answers whether the 25-33 ms p99 exists before any far-view change. It is not a reading of the finished site. Accepted by t-td."
$groundNote = "ground2-* is the clean repeat of the ground A-B-A (far 1000, 1200, 1000, authored fog); see run.json for the build it ran on."
$ridgeNote = "ridge2-stills: 1 km ground stills of the summit at far 1200 and authored fog, each with a summit-tile-hidden twin; the 5 second cab session they ride on is not a measurement. The tower centre is read from the site record, confirmed by level-designer: Data/terrain/build/map_vectors.json, landmark LookoutTower, centre = (x + site.offsetX, z + site.offsetZ), written by tools/terrain/build_terrain.py from tools/terrain/landmarks.json; the file's hash is in run.json and in the result's summit_source. Tower height is to the cab floor, from the tower's parameter table. Whether the ridge reads is game-director's judgement on the stills."

function ArmJson([string]$name) {
    $file = if ($name -like "ground*") { "bells-bend-fps-shore.json" } else { "farview-cab.json" }
    $path = Join-Path (Join-Path $ResultDir $name) $file
    if (Test-Path -LiteralPath $path) { return $path }
    return $null
}

function WriteSummary() {
    $rows = @()
    foreach ($name in $arms.Keys) {
        $path = ArmJson $name
        if (-not $path) { continue }
        $j = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $row = [ordered]@{ arm = $name; median_ms = $j.median_ms; p99_ms = $j.p99_ms; max_ms = $j.max_ms; samples = $j.samples; duration_s = $j.duration_s }
        $settings = Join-Path (Join-Path $ResultDir $name) "farview-settings.json"
        if ($name -like "ground*") {
            if (Test-Path -LiteralPath $settings) { $row.camera_far = (Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json).camera_far }
            $row.shore_leg_median_ms = $j.shore_leg.median_ms; $row.shore_leg_p99_ms = $j.shore_leg.p99_ms
        } else {
            $row.camera_far = $j.camera_far; $row.fog_start = $j.fog_start; $row.fog_end = $j.fog_end; $row.tree_distance = $j.tree_distance_max
            $row.draw_calls_median = $j.draw_calls.median; $row.draw_calls_max = $j.draw_calls.max
            $row.triangles_median = $j.triangles.median; $row.triangles_max = $j.triangles.max
            $row.tree_apply_ms = $j.tree_apply_ms; $row.tree_apply_hitch_ms = $j.tree_apply_hitch_ms
            $row.barrier_changed_px = $j.still.barrier_off.changed_px; $row.barrier_changed_px_over_8 = $j.still.barrier_off.changed_px_over_8
            $row.same_frame_noise_px = $j.still.repeat_same_frame.changed_px; $row.barrier_distance_m = $j.still.barrier_distance_m
            $row.lod_bias = $j.lod_bias; $row.quality = $j.quality
            if ($j.ridge_still) {
                $row.ridge_views = @($j.ridge_still.views | ForEach-Object { [ordered]@{ name = $_.name; bearing_from_summit_deg = $_.bearing_from_summit_deg; eye = $_.eye
                    changed_px = $_.summit_tile_off.changed_px; changed_px_over_8 = $_.summit_tile_off.changed_px_over_8; bbox_xyxy = $_.summit_tile_off.bbox_xyxy
                    mean_abs_diff_in_bbox = $_.summit_tile_off.mean_abs_diff_in_bbox_0_255; same_frame_noise_px = $_.repeat_same_frame.changed_px
                    chosen_by = $_.chosen_by; notes = $_.notes; distance_m = $_.distance_m; zone = $_.zone
                    ridge_line = $_.ridge_line; ridge_line_same_frame_repeat = $_.ridge_line_same_frame_repeat } })
            }
        }
        if ($name -like "cab2-*") { $row.note = $cabNote }
        if ($name -like "ground2-*") { $row.note = $groundNote }
        if ($name -like "ridge2-*") { $row.note = $ridgeNote }
        if ($name -like "near2-*") {
            $row.note = "near2-stills: near-plane regression stills, not a timing. Rule set in advance by technical-director: a near value passes if its twin against the authored still shows no pixel over 8/255 beyond what the same-frame repeat shows, at every pose; anything above goes to game-director with the stills."
            $row.authored_near = $j.near_stills.authored_near
            $row.near_poses = @($j.near_stills.poses | ForEach-Object { $p = $_; [ordered]@{ pose = $p.pose
                repeat_over_3 = $p.repeat_same_frame.changed_px_over_3; repeat_over_8 = $p.repeat_same_frame.changed_px_over_8
                values = @($p.values | ForEach-Object { [ordered]@{ near = $_.near_applied; over_3 = $_.vs_authored.changed_px_over_3; over_8 = $_.vs_authored.changed_px_over_8; bbox_xyxy = $_.vs_authored.bbox_xyxy } }) } })
        }
        $row.json = $path; $row.sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
        # An arm run beside a busy Unity CLI helper is void (the bar is in run.json; arms run before the bar existed have no entry).
        $runPath = Join-Path (Join-Path $ResultDir $name) "run.json"
        if (Test-Path -LiteralPath $runPath) {
            $rj = Get-Content -LiteralPath $runPath -Raw | ConvertFrom-Json
            if ($null -ne $rj.voidByCliHelper) { $row.void_by_cli_helper = $rj.voidByCliHelper; $row.cli_helper_void_reasons = @($rj.cliHelperVoidReasons) }
        }
        $rows += $row
    }
    # Same route and duration must give about the same number of frames: a ground arm more than 5% away from the
    # others of its set was measured under a different load and is void (qa-lead's rule).
    foreach ($set in "ground-", "ground2-") {
        $g = @($rows | Where-Object { $_.arm -like "$set*" })
        if ($g.Count -lt 2) { continue }
        $most = 0; foreach ($r in $g) { if ($r.samples -gt $most) { $most = $r.samples } }
        foreach ($r in $g) { $r.samples_vs_set_max = [math]::Round($r.samples / $most, 3); $r.void_by_sample_count = ($r.samples -lt 0.95 * $most) }
    }
    $table = Join-Path $ResultDir "farview-table.json"
    New-Item -ItemType Directory -Force -Path $ResultDir | Out-Null
    [System.IO.File]::WriteAllText($table, (ConvertTo-Json -InputObject @($rows) -Depth 6))
    $rows | ForEach-Object { [pscustomobject]$_ } | Format-Table arm, camera_far, tree_distance, median_ms, p99_ms, max_ms, draw_calls_median, triangles_median, barrier_changed_px -AutoSize | Out-String | Write-Host
    Write-Host "FARVIEW table=$table arms=$($rows.Count)/$($arms.Count)"
}

# --- what else may run during an arm ------------------------------------------------------------------------------
# Blender and ffmpeg never. Unity.exe is two different things: an Editor (interactive or batchmode), which must not run
# during an arm, and the Unity CLI's helper (--internal-auth-broker-serve: no project, no rendering), which may stay
# but is watched. They are told apart by command line; a Unity.exe whose command line is empty or cannot be read counts
# as an Editor, and so does one that carries the helper's flag together with anything an Editor run is started with.
# qa-lead's bar, set on 13 Oct before any ground2 result: an arm is void if a helper shows HelperCpuBar CPU-seconds per
# second or more at any sample, or more than HelperGpuBar percent of the GPU at the start or the end, or if its GPU
# share cannot be read then (the counter query fails; a helper with no GPU engine instance reads 0 and is not void).
$HelperPattern = '(^|\s)--internal-auth-broker-serve(\s|$)'; $EditorPattern = '(^|\s)-(projectpath|batchmode|executemethod)(\s|$)'; $HelperCpuBar = 0.2; $HelperGpuBar = 1.0
function UnityKind([string]$commandLine) {
    if ($commandLine -match $HelperPattern -and $commandLine -notmatch $EditorPattern) { return "helper" }
    return "editor"
}
# $ids: every process named Unity. $lines: process id -> command line, for those whose command line could be read.
# A process is a helper only if its line was read and says so; every other one is taken for an Editor.
function UnitySplitFrom($ids, $lines) {
    $editors = @(); $helpers = @()
    foreach ($id in $ids) {
        if ($lines -and $lines.ContainsKey($id) -and (UnityKind ([string]$lines[$id])) -eq "helper") { $helpers += $id } else { $editors += $id }
    }
    return @{ editors = $editors; helpers = $helpers }
}
function UnitySplit() {
    $ids = @(Get-Process Unity -ErrorAction SilentlyContinue | ForEach-Object { [int]$_.Id })
    $lines = @{}
    # Command lines are asked for only when a process named Unity exists, so the usual sample costs no more than before.
    if ($ids.Count -gt 0) {
        foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue)) { $lines[[int]$p.ProcessId] = [string]$p.CommandLine }
    }
    return UnitySplitFrom $ids $lines
}
function Heavy($split) {
    $list = @(Get-Process blender, ffmpeg -ErrorAction SilentlyContinue | ForEach-Object { "$($_.ProcessName):$($_.Id)" })
    foreach ($id in $split.editors) { $list += "Unity:$id" }
    return ,$list
}
# Each helper's CPU share (CPU-seconds per second over about a second) and, when asked, its share of the GPU.
# gpu_asked: a GPU reading was wanted at this sample (start and end; not at one minute). gpu_read: the counter query
# answered. When it answered, a process with no GPU engine instance reads 0 percent (gpu_engine_instances 0); when it
# did not answer, gpu_percent and gpu_engine_instances are null and the arm is void. A helper that exits between the two
# CPU readings has cpu_s_per_s null: that is "gone", not a figure that passed.
$GpuCounter = '\GPU Engine(*)\Utilization Percentage'
function HelperShare($ids, [bool]$withGpu, [string]$counter = $GpuCounter) {
    $rows = @()
    if ($ids.Count -eq 0) { return ,$rows }
    $c = [System.Diagnostics.Stopwatch]::StartNew(); $t = @{}
    foreach ($id in $ids) { $q = Get-Process -Id $id -ErrorAction SilentlyContinue; if ($q) { $t[$id] = $q.TotalProcessorTime.TotalSeconds } }
    $sample = $null
    if ($withGpu) { try { $sample = Get-Counter $counter -SampleInterval 1 -MaxSamples 1 -ErrorAction SilentlyContinue } catch { $sample = $null } }
    # Read means the query came back with its sample set; a failed query comes back with nothing at all.
    $gpuOk = $withGpu -and $null -ne $sample -and $null -ne $sample.CounterSamples
    $gpu = if ($gpuOk) { @($sample.CounterSamples | Where-Object { $null -ne $_ }) } else { @() }
    if ($c.Elapsed.TotalSeconds -lt 1.0) { Start-Sleep -Milliseconds ([int](1000 - $c.Elapsed.TotalMilliseconds)) }
    foreach ($id in $ids) {
        $q = Get-Process -Id $id -ErrorAction SilentlyContinue
        $cpuShare = if ($q -and $t.ContainsKey($id)) { [math]::Round(($q.TotalProcessorTime.TotalSeconds - $t[$id]) / $c.Elapsed.TotalSeconds, 3) } else { $null }
        $mine = @($gpu | Where-Object { $_.InstanceName -like "pid_${id}_*" })
        $gpuShare = if ($gpuOk) { [math]::Round([double]($mine | Measure-Object CookedValue -Sum).Sum, 2) } else { $null }
        $rows += [ordered]@{ id = $id; told_by = "--internal-auth-broker-serve"; cpu_s_per_s = $cpuShare; cpu_window_s = [math]::Round($c.Elapsed.TotalSeconds, 2)
            gpu_percent = $gpuShare; gpu_engine_instances = $(if ($gpuOk) { $mine.Count } else { $null }); gpu_asked = $withGpu; gpu_read = $gpuOk }
    }
    return ,$rows
}
# Why an arm is void under the helper bar; empty when it is not. A GPU share that was asked for and could not be read
# cannot pass the bar.
function HelperVoidReasons($samples) {
    $why = @()
    foreach ($when in $samples.Keys) {
        $s = $samples[$when]
        if (-not $s) { continue }
        foreach ($h in $s.unity_cli_helpers) {
            if ($null -ne $h.cpu_s_per_s -and $h.cpu_s_per_s -ge $HelperCpuBar) { $why += "Unity CLI helper $($h.id) used $($h.cpu_s_per_s) CPU-seconds per second at $when (bar $HelperCpuBar)" }
            if (-not $h.gpu_asked) { continue }
            if (-not $h.gpu_read -or $null -eq $h.gpu_percent) { $why += "Unity CLI helper $($h.id): GPU share could not be read at $when" }
            elseif ($h.gpu_percent -gt $HelperGpuBar) { $why += "Unity CLI helper $($h.id) used $($h.gpu_percent) percent of the GPU at $when (bar $HelperGpuBar)" }
        }
    }
    return ,$why
}

if ($List) { $arms.GetEnumerator() | ForEach-Object { Write-Host ("{0,-18} {1}" -f $_.Key, $_.Value) }; exit 0 }
if ($Summary) { WriteSummary; exit 0 }
if ($ProcessCheck) {
    $split = UnitySplit; $heavy = Heavy $split
    Write-Host ([ordered]@{ render_or_editor_processes = $heavy; unity_cli_helpers = (HelperShare $split.helpers $true) } | ConvertTo-Json -Depth 4 -Compress)
    if ($heavy.Count -gt 0) { Write-Host "FARVIEW REFUSED: heavy process running: $($heavy -join ', ')"; exit 2 }
    Write-Host "FARVIEW PROCESS CHECK OK: nothing running that refuses a launch"; exit 0
}
if (-not $arms.Contains($Arm)) { Write-Host "FARVIEW REFUSED: unknown arm '$Arm' (use -List)"; exit 2 }
if ($Arm -like "near2-*" -and (-not $site -or -not $cabEye)) { Write-Host "FARVIEW REFUSED: the site record or the tower height could not be read ($sitePath, $paramsPath)"; exit 2 }
if ($Arm -like "ridge2-*") {
    if (-not $site) { Write-Host "FARVIEW REFUSED: no sited LookoutTower landmark could be read from $sitePath"; exit 2 }
    if (-not $towerHeight) { Write-Host "FARVIEW REFUSED: the tower height could not be read from $paramsPath"; exit 2 }
}

# --- launch guards ---------------------------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $Exe)) { Write-Host "FARVIEW REFUSED: player not built: $Exe"; exit 2 }
if (Get-Process WashedAshorePOC -ErrorAction SilentlyContinue) { Write-Host "FARVIEW REFUSED: WashedAshorePOC is already running"; exit 2 }
$dir = Join-Path $ResultDir $Arm
$log = Join-Path $dir "Player.log"
if (Test-Path -LiteralPath $log) { Write-Host "FARVIEW REFUSED: $Arm already has a Player.log (one session per log); move or delete $dir first"; exit 2 }
# A disconnected console session renders to a virtual display and the numbers mean nothing.
$session = (query user 2>$null | Out-String)
if ($session -notmatch "(?m)^>.*\bconsole\b.*\bActive\b") { Write-Host "FARVIEW REFUSED: the console session is not active:`n$session"; exit 2 }
New-Item -ItemType Directory -Force -Path $dir | Out-Null

# What else is using the machine, at the start and the end of the arm: total CPU and the busiest other processes.
function Load() {
    $cpu = [math]::Round((Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 1 -MaxSamples 2).CounterSamples[-1].CookedValue, 1)
    $a = @{}; $t0 = @{}; $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Get-Process | ForEach-Object { $a[$_.Id] = $_.TotalProcessorTime.TotalSeconds; $t0[$_.Id] = $clock.Elapsed.TotalSeconds }
    Start-Sleep -Seconds 2
    $busy = @(Get-Process | Where-Object { $_.ProcessName -ne "WashedAshorePOC" -and $a.ContainsKey($_.Id) } | ForEach-Object {
        [pscustomobject]@{ name = $_.ProcessName; id = $_.Id; cpu_s_per_s = [math]::Round(($_.TotalProcessorTime.TotalSeconds - $a[$_.Id]) / [math]::Max(0.5, $clock.Elapsed.TotalSeconds - $t0[$_.Id]), 2) } } |
        Where-Object { $_.cpu_s_per_s -ge 0.2 } | Sort-Object cpu_s_per_s -Descending | Select-Object -First 10)
    $split = UnitySplit
    return [ordered]@{ time = (Get-Date).ToString("o"); cpu_total_percent = $cpu; busy_processes = $busy; render_or_editor_processes = (Heavy $split)
        unity_cli_helpers = (HelperShare $split.helpers $true) }
}
$loadStart = Load
if ($loadStart.render_or_editor_processes.Count -gt 0) { Write-Host "FARVIEW REFUSED: heavy process running: $($loadStart.render_or_editor_processes -join ', ')"; exit 2 }
# Launch guards again, immediately before the launch (the load sample above took a few seconds).
if (Get-Process WashedAshorePOC -ErrorAction SilentlyContinue) { Write-Host "FARVIEW REFUSED: WashedAshorePOC is already running"; exit 2 }
if ((query user 2>$null | Out-String) -notmatch "(?m)^>.*\bconsole\b.*\bActive\b") { Write-Host "FARVIEW REFUSED: the console session is not active"; exit 2 }
$argLine = "-monitor 1 $($arms[$Arm]) -logFile `"$log`""
$started = Get-Date
$proc = Start-Process -FilePath $Exe -ArgumentList $argLine -PassThru
$null = $proc.Handle
# One light sample a minute in: total CPU and whether a render or Editor has started (no per-process sweep mid-run).
$loadMid = $null
if (-not $proc.WaitForExit(60000)) {
    $splitMid = UnitySplit
    $loadMid = [ordered]@{ time = (Get-Date).ToString("o")
        cpu_total_percent = [math]::Round((Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 1 -MaxSamples 1).CounterSamples[-1].CookedValue, 1)
        render_or_editor_processes = (Heavy $splitMid)
        unity_cli_helpers = (HelperShare $splitMid.helpers $false) }   # CPU share only while the player runs (qa-lead)
}
$timedOut = -not $proc.WaitForExit($TimeoutSec * 1000)
if ($timedOut) { $proc.Kill(); $proc.WaitForExit() }
$finished = Get-Date
$loadEnd = Load
$helperWhy = HelperVoidReasons ([ordered]@{ "the start" = $loadStart; "one minute" = $loadMid; "the end" = $loadEnd })

$json = ArmJson $Arm
$sessions = if (Test-Path -LiteralPath $log) { @(Select-String -LiteralPath $log -Pattern "Initialize engine version").Count } else { 0 }
$proof = if (Test-Path -LiteralPath $log) { @(Select-String -LiteralPath $log -Pattern "\[FarView\]" | ForEach-Object { $_.Line }) } else { @() }
$run = [ordered]@{
    arm = $Arm; args = $argLine; exe = $Exe; exeSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $Exe).Hash.ToLowerInvariant()
    started = $started.ToString("o"); finished = $finished.ToString("o"); seconds = [math]::Round(($finished - $started).TotalSeconds, 1)
    exitCode = $proc.ExitCode; timedOut = $timedOut; sessionsInLog = $sessions; json = $json; farViewLogLines = $proof
    loadAtStart = $loadStart; loadAtOneMinute = $loadMid; loadAtEnd = $loadEnd
    voidByCliHelper = ($helperWhy.Count -gt 0); cliHelperVoidReasons = $helperWhy; cliHelperBar = [ordered]@{ cpu_s_per_s = $HelperCpuBar; gpu_percent_at_start_or_end = $HelperGpuBar }
    siteSource = $(if ($Arm -like "ridge2-*" -or $Arm -like "near2-*") { $siteSource }); towerHeightSource = $(if ($Arm -like "ridge2-*" -or $Arm -like "near2-*") { $towerHeightSource })
}
[System.IO.File]::WriteAllText((Join-Path $dir "run.json"), ($run | ConvertTo-Json -Depth 6))
$problems = @()
if ($timedOut) { $problems += "timed out after $TimeoutSec s" }
if ($proc.ExitCode -ne 0) { $problems += "player exit code $($proc.ExitCode)" }
if (-not $json) { $problems += "no result JSON beside Player.log" }
if ($sessions -ne 1) { $problems += "$sessions sessions in Player.log (expected 1)" }
foreach ($w in $helperWhy) { $problems += "VOID: $w" }
if ($proof.Count -eq 0) { $problems += "no [FarView] line in Player.log, so the arm's settings are not proven" }
if ($Arm -like "ground*" -and -not (Test-Path -LiteralPath (Join-Path $dir "farview-settings.json"))) { $problems += "no farview-settings.json, so the ground arm's far plane is not recorded" }
if ($Arm -like "near2-*" -and $json -and -not (Select-String -LiteralPath $json -Pattern '"authored_near"' -Quiet)) { $problems += "the result has no near_stills: this player does not contain the near-plane probe" }
if ($Arm -like "ridge2-*") {
    if (-not $towerHeight) { $problems += "tower height could not be read from $paramsPath" }
    # The ridge-line measure only exists in the revised probe: without it this player is the old build.
    if ($json -and -not (Select-String -LiteralPath $json -Pattern '"ridge_line"' -Quiet)) { $problems += "the result has no ridge_line: this player does not contain the revised probe" }
}
if ($problems.Count -gt 0) { Write-Host "FARVIEW FAILED ${Arm}: $($problems -join '; ')"; exit 1 }
Write-Host "FARVIEW OK $Arm seconds=$($run.seconds) json=$json"
exit 0
