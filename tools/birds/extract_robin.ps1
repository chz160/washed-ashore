# Extracts only what Unity needs from the SoltorchGames American Robin free sample into
# Assets/ThirdParty/SoltorchGames/AmericanRobin/: the FBX, its texture, LICENSE.txt and README.txt.
# The GLB and .blend copies are left in the zip. Repeatable; never writes vendor/.
# License: the files may not be redistributed, so this folder must only live in a private repo
# (github.com/chz160/washed-ashore is private) or be git-ignored.
# Usage: pwsh tools/birds/extract_robin.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$zipPath = Join-Path $repo "vendor\LookToTheBirds_FreeSample_AmericanRobin_v1.0.zip"
$dest = Join-Path $repo "Assets\ThirdParty\SoltorchGames\AmericanRobin"
$wanted = @{
    "LookToTheBirds_FreeSample_AmericanRobin_v1.0/FBX/American_Robin_Mesh.fbx"          = "American_Robin_Mesh.fbx"
    "LookToTheBirds_FreeSample_AmericanRobin_v1.0/Textures/American_Robin_Tex.png"     = "American_Robin_Tex.png"
    "LookToTheBirds_FreeSample_AmericanRobin_v1.0/LICENSE.txt"                         = "LICENSE.txt"
    "LookToTheBirds_FreeSample_AmericanRobin_v1.0/README.txt"                          = "README.txt"
}
New-Item -ItemType Directory -Force -Path $dest | Out-Null
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
