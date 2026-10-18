<#
.SYNOPSIS
    Releases the macOS build (unsigned .app in a zip, with README-macOS.txt).
    Same as: .\release-windows.ps1 -Platform mac

.DESCRIPTION
    Forwards every argument to release-windows.ps1. Refuses with a clear message when
    Builds\Mac has no WashedAshore.app (build it first; the Mac Build Support module must be installed).
#>
& (Join-Path $PSScriptRoot "release-windows.ps1") -Platform mac @args
exit $LASTEXITCODE
