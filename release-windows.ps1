<#
.SYNOPSIS
    Publishes the Windows, Linux and macOS builds as assets of one GitHub release.

.DESCRIPTION
    For each platform, copies its build folder (Builds/Windows, Builds/Linux,
    Builds/Mac) to a staging folder, leaving out the folders Unity marks
    "DoNotShip", build provenance and log files, and packages it:

        Windows  WashedAshore-<version>-win64.zip
        Linux    washed-ashore_<version>_amd64.deb
                 Installs to /opt/washed-ashore, adds a "washed-ashore" command
                 and a menu entry. Built here, so dpkg is not needed.
        macOS    WashedAshore-<version>-mac.zip, holding WashedAshore.app. The
                 executables inside keep their execute bit. The app is not code
                 signed or notarized, so players right-click > Open the first time.

    All the packages go on one release, created with the gh CLI. The version is
    the current time as yyyy.MM.dd.HHmm, for example 2026.10.06.1430, and the
    tag is that version with a "v" in front. The tag points at the current
    commit, which must already be pushed.

.PARAMETER Platform
    Which platforms to release: windows, linux, mac. Give a list to pick some,
    for example -Platform windows,linux. Leave it off to release all three.

.PARAMETER Build
    Rebuild the selected players with the unity CLI before releasing. The Unity
    editor needs the Build Support module for each platform installed.

.PARAMETER Notes
    Release notes. Defaults to the commit the release is tagged on.

.PARAMETER Prerelease
    Mark the release as a pre-release.

.PARAMETER StageOnly
    Build the packages into Builds/release-local and stop. Does not need gh, does not tag or upload anything.

.PARAMETER Draft
    Create the release as a draft, so it is not public until you publish it on GitHub.

.EXAMPLE
    .\release-windows.ps1
    .\release-windows.ps1 -Build -Notes "Added birds"
    .\release-windows.ps1 -Platform windows,mac -Prerelease
#>
[CmdletBinding()]
param(
    [string[]]$Platform = @("windows", "linux", "mac"),
    [switch]$Build,
    [string]$Notes,
    [switch]$Prerelease,
    [switch]$Draft,
    [switch]$StageOnly
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$repo = "chz160/washed-ashore"
$version = Get-Date -Format "yyyy.MM.dd.HHmm"
$tag = "v$version"
# "-File" hands a list over as one comma-joined string, so split it here.
$requested = @($Platform | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim().ToLower() } | Where-Object { $_ })
$unknown = @($requested | Where-Object { @("windows", "linux", "mac") -notcontains $_ })
if ($unknown) { throw "Unknown platform '$($unknown -join ', ')'. Use windows, linux and/or mac." }
$platforms = @("windows", "linux", "mac") | Where-Object { $requested -contains $_ }

$targets = @{
    windows = @{ Label = "Windows"; UnityTarget = "StandaloneWindows64"; Dir = "Windows"; Output = "WashedAshorePOC.exe" }
    linux   = @{ Label = "Linux";   UnityTarget = "StandaloneLinux64";   Dir = "Linux";   Output = "WashedAshore.x86_64" }
    mac     = @{ Label = "macOS";   UnityTarget = "StandaloneOSX";       Dir = "Mac";     Output = "WashedAshore.app" }
}

# Writes the .deb. A .deb is an "ar" archive of debian-binary, control.tar.gz and data.tar.gz, and
# its tar members need Unix modes and root ownership, which Windows' own tools cannot set.
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;

public sealed class DebTar : IDisposable
{
    private readonly Stream _out;
    public DebTar(Stream output) { _out = output; }

    public void AddDir(string path) { Header(path.TrimEnd('/') + "/", 0, 493, '5', ""); }          // 0755

    public void AddSymlink(string path, string target) { Header(path, 0, 511, '2', target); }      // 0777

    public void AddFile(string path, string source, bool executable)
    {
        long size = new FileInfo(source).Length;
        Header(path, size, executable ? 493 : 420, '0', "");                                       // 0755 / 0644
        using (FileStream fs = File.OpenRead(source)) { fs.CopyTo(_out); }
        Pad(size);
    }

    public void AddBytes(string path, byte[] data, bool executable)
    {
        Header(path, data.Length, executable ? 493 : 420, '0', "");
        _out.Write(data, 0, data.Length);
        Pad(data.Length);
    }

    public void Dispose()
    {
        _out.Write(new byte[1024], 0, 1024);   // two empty blocks end the archive
        _out.Flush();
    }

    private void Pad(long size)
    {
        int rem = (int)(size % 512);
        if (rem != 0) _out.Write(new byte[512 - rem], 0, 512 - rem);
    }

    private void Header(string path, long size, int mode, char type, string link)
    {
        // ustar splits a long path into a 155-byte prefix and a 100-byte name at a slash.
        string name = path, prefix = "";
        if (Encoding.UTF8.GetByteCount(name) > 100)
        {
            int cut = -1;
            for (int i = 0; i < path.Length - 1; i++)
            {
                if (path[i] == '/' && Encoding.UTF8.GetByteCount(path.Substring(0, i)) <= 155
                    && Encoding.UTF8.GetByteCount(path.Substring(i + 1)) <= 100) cut = i;
            }
            if (cut < 0) throw new InvalidOperationException("Path too long for a tar header: " + path);
            prefix = path.Substring(0, cut);
            name = path.Substring(cut + 1);
        }
        if (Encoding.UTF8.GetByteCount(link) > 100) throw new InvalidOperationException("Link target too long: " + link);

        byte[] h = new byte[512];
        Put(h, 0, 100, name);
        Octal(h, 100, 8, mode);
        Octal(h, 108, 8, 0);          // uid
        Octal(h, 116, 8, 0);          // gid
        Octal(h, 124, 12, size);
        Octal(h, 136, 12, 0);         // mtime: 1970 keeps the package reproducible
        for (int i = 148; i < 156; i++) h[i] = (byte)' ';
        h[156] = (byte)type;
        Put(h, 157, 100, link);
        Put(h, 257, 6, "ustar");
        h[263] = (byte)'0'; h[264] = (byte)'0';
        Put(h, 265, 32, "root");
        Put(h, 297, 32, "root");
        Put(h, 345, 155, prefix);

        long sum = 0;
        for (int i = 0; i < 512; i++) sum += h[i];
        string chk = Convert.ToString(sum, 8).PadLeft(6, '0');
        Put(h, 148, 6, chk);
        h[154] = 0; h[155] = (byte)' ';
        _out.Write(h, 0, 512);
    }

    private static void Put(byte[] h, int offset, int len, string s)
    {
        byte[] b = Encoding.UTF8.GetBytes(s);
        Array.Copy(b, 0, h, offset, Math.Min(b.Length, len));
    }

    private static void Octal(byte[] h, int offset, int len, long value)
    {
        string s = Convert.ToString(value, 8).PadLeft(len - 1, '0');
        if (s.Length > len - 1) throw new InvalidOperationException("Value too large for a tar field: " + value);
        Put(h, offset, len - 1, s);
    }

    public static void WriteAr(string archive, string[] names, string[] files)
    {
        using (FileStream o = File.Create(archive))
        {
            byte[] magic = Encoding.ASCII.GetBytes("!<arch>\n");
            o.Write(magic, 0, magic.Length);
            for (int i = 0; i < names.Length; i++)
            {
                long size = new FileInfo(files[i]).Length;
                string head = (names[i] + "/").PadRight(16) + "0".PadRight(12) + "0".PadRight(6) + "0".PadRight(6)
                    + "100644".PadRight(8) + size.ToString().PadRight(10) + "`\n";
                byte[] hb = Encoding.ASCII.GetBytes(head);
                o.Write(hb, 0, hb.Length);
                using (FileStream fs = File.OpenRead(files[i])) { fs.CopyTo(o); }
                if (size % 2 == 1) o.WriteByte((byte)'\n');
            }
        }
    }
}
'@

# Zip a folder under a top-level folder name. Unlike ZipFile.CreateFromDirectory, this records Unix
# permissions, so executables stay executable on macOS and Linux.
Add-Type -AssemblyName System.IO.Compression
function New-ReleaseZip {
    param([string]$SourceDir, [string]$TopName, [string]$ZipPath, [scriptblock]$IsExecutable)
    $src = (Resolve-Path $SourceDir).Path.TrimEnd("\", "/")
    $fs = [IO.File]::Create($ZipPath)
    try {
        $zip = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($f in Get-ChildItem $src -Recurse -File -Force) {
                $rel = $f.FullName.Substring($src.Length + 1).Replace("\", "/")
                $entry = $zip.CreateEntry("$TopName/$rel", [IO.Compression.CompressionLevel]::Optimal)
                $mode = if (& $IsExecutable $rel) { 493 } else { 420 }   # 0755 / 0644
                $entry.ExternalAttributes = (32768 + $mode) -shl 16      # S_IFREG + mode
                $entry.LastWriteTime = $f.LastWriteTime
                $in = [IO.File]::OpenRead($f.FullName)
                $out = $entry.Open()
                try { $in.CopyTo($out) } finally { $out.Dispose(); $in.Dispose() }
            }
        } finally { $zip.Dispose() }
    } finally { $fs.Dispose() }
}

function Compress-Gzip {
    param([string]$Path, [string]$Destination)
    $in = [IO.File]::OpenRead($Path)
    $out = [IO.File]::Create($Destination)
    try {
        $gz = New-Object IO.Compression.GZipStream($out, [IO.Compression.CompressionLevel]::Optimal)
        try { $in.CopyTo($gz) } finally { $gz.Dispose() }
    } finally { $out.Dispose(); $in.Dispose() }
}

# Copies a build folder, leaving out what must not ship.
function Copy-CleanBuild {
    param([string]$Source, [string]$Destination)
    Copy-Item $Source $Destination -Recurse
    Get-ChildItem $Destination -Recurse -Directory | Where-Object Name -match "DoNotShip|DontShip" |
        Sort-Object { $_.FullName.Length } -Descending | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem $Destination -Recurse -Include "*.provenance.json", "*.log" | Remove-Item -Force
}

function Find-Player {
    param([string]$Key, [string]$Dir)
    switch ($Key) {
        "windows" {
            Get-ChildItem $Dir -Filter "*.exe" -File -ErrorAction SilentlyContinue |
                Where-Object Name -ne "UnityCrashHandler64.exe" | Select-Object -First 1
        }
        "linux" { Get-ChildItem $Dir -Filter "*.x86_64" -File -ErrorAction SilentlyContinue | Select-Object -First 1 }
        "mac" { Get-ChildItem $Dir -Filter "*.app" -Directory -ErrorAction SilentlyContinue | Select-Object -First 1 }
    }
}

function New-DebPackage {
    param([string]$Stage, [string]$Player, [string]$DebPath, [string]$Work)

    $gameDir = Join-Path $Stage "WashedAshore"
    $installDir = "opt/washed-ashore"

    $dataTar = Join-Path $Work "data.tar"
    $fs = [IO.File]::Create($dataTar)
    $tar = New-Object DebTar($fs)
    $installedBytes = 0
    try {
        foreach ($d in "opt", $installDir, "usr", "usr/bin", "usr/share", "usr/share/applications") { $tar.AddDir($d) }
        foreach ($d in Get-ChildItem $gameDir -Recurse -Directory) {
            $tar.AddDir("$installDir/" + $d.FullName.Substring($gameDir.Length + 1).Replace("\", "/"))
        }
        foreach ($f in Get-ChildItem $gameDir -Recurse -File) {
            $rel = $f.FullName.Substring($gameDir.Length + 1).Replace("\", "/")
            $exec = ($f.Name -eq $Player) -or ($f.Extension -eq ".so")
            $tar.AddFile("$installDir/$rel", $f.FullName, $exec)
            $installedBytes += $f.Length
        }
        $tar.AddSymlink("usr/bin/washed-ashore", "/$installDir/$Player")
        $desktop = "[Desktop Entry]`nType=Application`nName=Washed Ashore`nComment=Washed Ashore`n" +
            "Exec=/$installDir/$Player`nPath=/$installDir`nIcon=applications-games`nCategories=Game;`nTerminal=false`n"
        $tar.AddBytes("usr/share/applications/washed-ashore.desktop", [Text.Encoding]::UTF8.GetBytes($desktop), $false)
    } finally { $tar.Dispose(); $fs.Dispose() }

    $maintainer = "$(& git -C $root config user.name) <$(& git -C $root config user.email)>"
    if ($maintainer -eq " <>") { $maintainer = "Washed Ashore <noreply@example.com>" }
    $control = "Package: washed-ashore`nVersion: $version`nSection: games`nPriority: optional`n" +
        "Architecture: amd64`nInstalled-Size: $([int][Math]::Ceiling($installedBytes / 1024))`n" +
        "Maintainer: $maintainer`nDepends: libc6`nDescription: Washed Ashore`n A game of survival and exploration.`n"
    $controlTar = Join-Path $Work "control.tar"
    $fs = [IO.File]::Create($controlTar)
    $tar = New-Object DebTar($fs)
    try {
        $tar.AddDir(".")
        $tar.AddBytes("control", [Text.Encoding]::UTF8.GetBytes($control), $false)
    } finally { $tar.Dispose(); $fs.Dispose() }

    $parts = Join-Path $Work "deb-parts"
    New-Item -ItemType Directory $parts | Out-Null
    [IO.File]::WriteAllText((Join-Path $parts "debian-binary"), "2.0`n", (New-Object Text.UTF8Encoding($false)))
    Compress-Gzip $controlTar (Join-Path $parts "control.tar.gz")
    Compress-Gzip $dataTar (Join-Path $parts "data.tar.gz")
    [DebTar]::WriteAr($DebPath,
        [string[]]@("debian-binary", "control.tar.gz", "data.tar.gz"),
        [string[]]@((Join-Path $parts "debian-binary"), (Join-Path $parts "control.tar.gz"), (Join-Path $parts "data.tar.gz")))
}

if (-not $StageOnly -and -not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "gh not found. Install the GitHub CLI from https://cli.github.com, then run 'gh auth login'."
}
if (-not $StageOnly) {
    & gh auth status *> $null
    if ($LASTEXITCODE -ne 0) { throw "gh is not logged in. Run 'gh auth login' first." }
}

# The tag is created on GitHub, so the commit has to be there already.
$commit = (& git -C $root rev-parse HEAD).Trim()
if (-not $StageOnly -and -not (& git -C $root branch -r --contains $commit)) {
    throw "Commit $($commit.Substring(0, 7)) is not on GitHub yet. Push it first."
}
if (& git -C $root status --porcelain --untracked-files=no) {
    Write-Warning "The working tree has uncommitted changes. The release is tagged on $($commit.Substring(0, 7)), which may not match the build."
}

if ($Build) {
    foreach ($key in $platforms) {
        $t = $targets[$key]
        $log = "Builds\build-$($t.Dir.ToLower())-release.log"
        Write-Host "Building the $($t.Label) player (this takes a few minutes)..." -ForegroundColor Cyan
        & unity build $root `
            --target $t.UnityTarget `
            --output-path (Join-Path $root "Builds\$($t.Dir)\$($t.Output)") `
            --log-file (Join-Path $root $log) `
            --no-tail --timeout 3600
        if ($LASTEXITCODE -ne 0) { throw "Unity build for $($t.Label) failed. See $log." }
    }
}

# Check every build exists before packaging anything.
$players = @{}
foreach ($key in $platforms) {
    $t = $targets[$key]
    $dir = Join-Path $root "Builds\$($t.Dir)"
    $player = Find-Player $key $dir
    if (-not $player) { throw "No $($t.Label) build at $dir. Build it first, or run with -Build." }
    $players[$key] = $player
}

$stageRoot = Join-Path ([IO.Path]::GetTempPath()) "washed-ashore-release"
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory $stageRoot | Out-Null

$assets = @()
foreach ($key in $platforms) {
    $t = $targets[$key]
    $stage = Join-Path $stageRoot $key
    New-Item -ItemType Directory $stage | Out-Null
    Copy-CleanBuild (Join-Path $root "Builds\$($t.Dir)") (Join-Path $stage "WashedAshore")
    Write-Host "Packaging $($t.Label)..." -ForegroundColor Cyan

    switch ($key) {
        "windows" {
            $asset = Join-Path $stageRoot "WashedAshore-$version-win64.zip"
            New-ReleaseZip (Join-Path $stage "WashedAshore") "WashedAshore" $asset { param($rel) $false }
        }
        "mac" {
            $asset = Join-Path $stageRoot "WashedAshore-$version-mac.zip"
            # Unsigned build: ship the how-to-open note next to the app.
            Copy-Item (Join-Path $root "tools/release/README-macOS.txt") (Join-Path $stage "WashedAshore/README-macOS.txt") -Force
            # Unity's player binary and its native libraries must stay executable inside the bundle.
            New-ReleaseZip (Join-Path $stage "WashedAshore") "WashedAshore" $asset {
                param($rel) $rel -match "/Contents/MacOS/" -or $rel -match "\.(dylib|bundle)$"
            }
        }
        "linux" {
            $asset = Join-Path $stageRoot "washed-ashore_${version}_amd64.deb"
            New-DebPackage $stage $players[$key].Name $asset $stage
        }
    }

    # GitHub rejects release assets of 2 GiB or more.
    $mb = (Get-Item $asset).Length / 1MB
    if ($mb -ge 2048) { throw ("{0} is {1:N0} MB, over GitHub's 2 GiB limit for a release file." -f (Split-Path $asset -Leaf), $mb) }
    Write-Host ("  {0} ({1:N1} MB)" -f (Split-Path $asset -Leaf), $mb)
    $assets += $asset
}

if ($StageOnly) {
    $out = Join-Path $root "Builds/release-local"
    New-Item -ItemType Directory $out -Force | Out-Null
    foreach ($a in $assets) { Copy-Item $a $out }
    Write-Host "Staged locally in $out (nothing uploaded):" -ForegroundColor Green
    Get-ChildItem $out | ForEach-Object { Write-Host ("  {0} ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB)) }
    return
}

if (-not $Notes) {
    $names = ($platforms | ForEach-Object { $targets[$_].Label }) -join ", "
    $Notes = "$names build of $(& git -C $root log -1 --format='%h %s' $commit)"
}
Write-Host "Releasing $tag ($(($platforms | ForEach-Object { $targets[$_].Label }) -join ', '))..." -ForegroundColor Cyan
$releaseArgs = @("release", "create", $tag) + $assets + @(
    "--repo", $repo,
    "--target", $commit,
    "--title", "Washed Ashore $version",
    "--notes", $Notes
)
if ($Prerelease) { $releaseArgs += "--prerelease" }
if ($Draft) { $releaseArgs += "--draft" }
$url = & gh @releaseArgs
if ($LASTEXITCODE -ne 0) { throw "gh release create failed (exit $LASTEXITCODE)." }

Write-Host ""
Write-Host "Released at $url" -ForegroundColor Green
