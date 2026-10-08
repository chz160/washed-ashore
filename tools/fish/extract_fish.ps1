# Extracts only the two freshwater stand-ins from the Quaternius Animated Fish Pack (CC0) into
# Assets/ThirdParty/Quaternius/AnimatedFishPack/: Fish1.fbx, Fish2.fbx and License.txt.
# Fish3 (clownfish), Shark, Whale, Dolphin and Manta ray stay in the zip (greenlight/fish: marine models excluded).
# The OBJ and .blend copies stay in the zip too. Repeatable; never writes vendor/.
# Usage: pwsh tools/fish/extract_fish.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$zipPath = Join-Path $repo "vendor\Animated Fish Pack by @Quaternius-20261007T190835Z-1-001.zip"
$dest = Join-Path $repo "Assets\ThirdParty\Quaternius\AnimatedFishPack"
$wanted = [ordered]@{
    "Animated Fish Pack by @Quaternius/FBX/Fish1.fbx" = "FBX\Fish1.fbx"
    "Animated Fish Pack by @Quaternius/FBX/Fish2.fbx" = "FBX\Fish2.fbx"
    "Animated Fish Pack by @Quaternius/License.txt"   = "License.txt"
}
New-Item -ItemType Directory -Force -Path (Join-Path $dest "FBX") | Out-Null
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($entry in $wanted.Keys) {
        $e = $zip.GetEntry($entry)
        if (-not $e) { throw "Missing in zip: $entry" }
        $target = Join-Path $dest $wanted[$entry]
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $target, $true)
        Write-Host "extracted $($wanted[$entry]) ($($e.Length) bytes)"
    }
} finally { $zip.Dispose() }
