# Re-exports the Pigeon FBX from the vendor .blend (see export_pigeon.py). Repeatable; never writes vendor/.
# Usage (repo root or anywhere): pwsh tools/blender/export_pigeon.ps1 [-Blender <path to blender.exe>] [-Out <dir>]
param(
    [string]$Blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe",
    [string]$Out = "Assets/ThirdParty/PeripheralArbor/Pigeon"
)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$blend = Join-Path $repo "vendor\Pigeon - Peripheral Arbor (Public Domain)\bird.blend"
$outDir = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $repo $Out }
if (-not (Test-Path $Blender)) { throw "Blender not found at '$Blender' (Blender 5.2.2 expected)" }
if (-not (Test-Path $blend)) { throw "Source not found: $blend" }

# The FBX exporter derives node IDs from Python's string hash; a fixed seed keeps re-exports byte-identical
# apart from the FBX creation timestamp. Blender ignores PYTHON* variables without --python-use-system-env.
$env:PYTHONHASHSEED = "0"
$log = & $Blender -b $blend --python-use-system-env --python (Join-Path $PSScriptRoot "export_pigeon.py") -- --out $Out 2>&1
$log | Where-Object { $_ -match "EXPORT_PIGEON|Error|Traceback" } | ForEach-Object { Write-Host $_ }
if (-not ($log -match "EXPORT_PIGEON OK")) { $log | Select-Object -Last 40 | Write-Host; exit 1 }

# The public-domain dedication travels with the asset.
Copy-Item (Join-Path (Split-Path $blend) "LICENSE-public-domain-dedication.html") $outDir -Force
exit 0
