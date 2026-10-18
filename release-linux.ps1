<#
.SYNOPSIS
    Releases the Linux build (.deb). Same as: .\release-windows.ps1 -Platform linux

.DESCRIPTION
    Forwards every argument to release-windows.ps1. Refuses with a clear message when
    Builds\Linux has no player (build it first; the Linux Build Support module must be installed).
#>
& (Join-Path $PSScriptRoot "release-windows.ps1") -Platform linux @args
exit $LASTEXITCODE
