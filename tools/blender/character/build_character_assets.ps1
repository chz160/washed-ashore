# Rebuilds the character art inputs: extracts the chosen outfits from the Quaternius modular packs (CC0) and
# authors the missing stance clips with Blender (author_clips.py), then writes the clip manifest.
# Repeatable; never writes vendor/. Does not start Unity.
# Usage: pwsh tools/blender/character/build_character_assets.ps1 [-AssetsRoot <dir>] [-SkipBlender] [-Reports <dir>]
#   -AssetsRoot  where Assets/ThirdParty/... and Assets/Art/... go (default: the repo's Assets). Use another dir to stage.
param(
    [string]$AssetsRoot = "",
    [string]$Reports = "",
    [string]$Manifest = "",
    [switch]$SkipBlender,
    [double]$ScaleMen = 1.030,    # uniform skeleton scale on the men's body (td: (1.65 + 0.05) / measured stand eye); women stay 1
    [double]$EyeBias = 0.05  # body drop below the player origin (m); clip eye = stance eye + this
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
if (-not $AssetsRoot) { $AssetsRoot = Join-Path $repo "Assets" }
if (-not $Manifest) { $Manifest = Join-Path $repo "_bmad-output\poc\character-clip-manifest.md" }
if (-not $Reports) { $Reports = Join-Path $repo "_bmad-output\poc\character-clips" }
$blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"

# Outfits: director's ruling (gate/player-character-phase0). Excluded: Suit, Swat, Soldier, Punk, King, SpaceSuit, SciFi, Medieval, Witch, Beach.
$packs = @(
    @{ Key = "Men"; Zip = "Ultimate Modular Men- Feb 2022-20261005T045546Z-1-001.zip"; Root = "Ultimate Modular Men- Feb 2022";
       Outfits = @(@("Worker", "Worker"), @("Farmer", "Farmer"), @("Casual_Hoodie", "Casual"), @("Casual_2", "Casual2"), @("Adventurer", "Adventurer")) },
    @{ Key = "Women"; Zip = "Ultimate Modular Women - April 2022-20261005T045539Z-1-001.zip"; Root = "Ultimate Modular Women - April 2022";
       Outfits = @(@("Worker", "Worker"), @("Casual", "Casual"), @("Adventurer", "Adventurer"), @("Formal", "Formal")) }
)
$sep = "Separate Skeletal Meshes and Animations"
$dest = Join-Path $AssetsRoot "ThirdParty\Quaternius\ModularCharacters"
$clips = Join-Path $AssetsRoot "Art\Character\Clips"
New-Item -ItemType Directory -Force -Path $clips, $Reports | Out-Null

foreach ($p in $packs) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Join-Path $repo "vendor\$($p.Zip)"))
    try {
        $wanted = [ordered]@{}
        $wanted["$($p.Root)/License.txt"] = "$($p.Key)\License.txt"
        $wanted["$($p.Root)/$sep/Animations.fbx"] = "$($p.Key)\Animations.fbx"
        foreach ($o in $p.Outfits) {
            foreach ($slot in "Head", "Body", "Legs", "Feet") {
                $wanted["$($p.Root)/$sep/$($o[0])/$($o[1])_$slot.fbx"] = "$($p.Key)\Parts\$($o[0])\$($o[1])_$slot.fbx"
            }
        }
        foreach ($entry in $wanted.Keys) {
            $e = $zip.GetEntry($entry)
            if (-not $e) { throw "Missing in zip: $entry" }
            $target = Join-Path $dest $wanted[$entry]
            New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $target, $true)
        }
        Write-Host "$($p.Key): extracted $($wanted.Count) files"
    } finally { $zip.Dispose() }
}

if (-not $SkipBlender) {
    foreach ($p in $packs) {
        $k = $p.Key
        $src = Join-Path $dest "$k\Animations.fbx"
        # eye point measured from the head meshes; the default player is Men Farmer, Women use Worker
        $heads = Get-ChildItem (Join-Path $dest "$k\Parts") -Recurse -Filter "*_Head.fbx" | ForEach-Object { $_.FullName }
        $eyeJson = Join-Path $Reports "eye-point-$k.json"
        & $blender -b -noaudio -P (Join-Path $PSScriptRoot "measure_head_eye.py") -- --out $eyeJson @heads
        $eyeKey = if ($k -eq "Men") { "Farmer_Head.fbx" } else { "Worker_Head.fbx" }
        $out = Join-Path $clips "Authored_$k.fbx"
        & $blender -b -noaudio -P (Join-Path $PSScriptRoot "author_clips.py") -- --src $src --out $out --report (Join-Path $Reports "authored-$k.json") --eye-bias $EyeBias --scale $(if ($k -eq 'Men') { $ScaleMen } else { 1.0 }) --eye-json $eyeJson --eye-key $eyeKey
        if ($LASTEXITCODE -ne 0) { throw "Blender author_clips failed for $k" }
        # measure what Unity will read: pack clips and authored clips, from the files themselves
        & $blender -b -noaudio -P (Join-Path $PSScriptRoot "measure_clips.py") -- --src $src --report (Join-Path $Reports "measured-pack-$k.json") --eye-json $eyeJson --eye-key $eyeKey
        $ar = Get-Content (Join-Path $Reports "authored-$k.json") -Raw | ConvertFrom-Json
        $scaleK = if ($k -eq 'Men') { $ScaleMen } else { 1.0 }
        $camera = "0,$($ar.eye_anchor_y),$([math]::Round((1.65 + $EyeBias) / $scaleK, 4))"  # the camera: stand eye, fixed (it never follows the body)
        & $blender -b -noaudio -P (Join-Path $PSScriptRoot "measure_clips.py") -- --src $out --report (Join-Path $Reports "measured-authored-$k.json") --eye-json $eyeJson --eye-key $eyeKey --camera $camera
    }
    $py = if (Get-Command python -ErrorAction SilentlyContinue) { "python" } else { "C:\Python313\python.exe" }
    & $py (Join-Path $PSScriptRoot "make_manifest.py") $Reports $Manifest
}
