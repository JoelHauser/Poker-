<#
.SYNOPSIS
    Packs the server mod into something that extracts straight into an SPT install.

.DESCRIPTION
    Produces SPT_Runtime/user/mods/Poker/, matching the layout Blackjack ships, so
    the zip is extracted at the root of the install and lands where SPT looks.

    There is no BepInEx half yet. The client plugin has to be compiled against the
    Assembly-CSharp.dll and spt-* DLLs of the install it will run on -- 4.1.3's
    PluginValidator checks the major.minor of those references -- so it cannot be
    built on a machine without the game on it.

.EXAMPLE
    ./scripts/pack-mod.ps1
    ./scripts/pack-mod.ps1 -InstallPath 'H:\SPT4.1.X'
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',

    # Copies straight into an install instead of only zipping. Handy on the box with
    # the game on it.
    [string] $InstallPath
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Poker.Server/Poker.Server.csproj'
$build = Join-Path $root 'dist/mod-build'
$stage = Join-Path $root 'dist/mod'
$modFolder = Join-Path $stage 'SPT_Runtime/user/mods/Poker'

foreach ($path in @($build, $stage)) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

Write-Host 'Building the server mod...' -ForegroundColor Cyan
& dotnet build $project -c $Configuration -o $build --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path $modFolder | Out-Null

# Only this mod's own assemblies. SPT provides its own, and shipping a second copy of
# them into the same process is a load conflict looking for somewhere to happen.
$wanted = @('Poker.Server.dll', 'Poker.Server.pdb', 'Poker.Game.dll', 'Poker.Game.pdb')

foreach ($name in $wanted) {
    $source = Join-Path $build $name
    if (Test-Path $source) {
        Copy-Item $source -Destination $modFolder
    }
    else {
        Write-Warning "missing $name"
    }
}

Copy-Item (Join-Path $root 'src/Poker.Server/config.json') -Destination $modFolder

# Version comes from the metadata, which must agree with the csproj. Read it back
# rather than hardcoding it here, so there is one fewer place to drift.
$metadata = Get-Content (Join-Path $root 'src/Poker.Server/ModMetadata.cs') -Raw
$version = if ($metadata -match 'new\("(?<v>\d+\.\d+\.\d+)"\)') { $Matches['v'] } else { '0.0.0' }

Write-Host "Staged v${version}:" -ForegroundColor Green
Get-ChildItem $modFolder | ForEach-Object { Write-Host ("  {0,9:N0}  {1}" -f $_.Length, $_.Name) }

$releases = Join-Path $root 'releases'
New-Item -ItemType Directory -Force -Path $releases | Out-Null
$archive = Join-Path $releases "Poker-$version-SPT4.1.zip"
if (Test-Path $archive) { Remove-Item $archive -Force }

# Entries are written one at a time, with forward slashes, deliberately.
#
# Compress-Archive writes backslash entry names, which extract on Linux as a single
# file literally called "SPT_Runtime\user\mods\Poker\config.json". That much was
# already known from Blackjack. What was not is that ZipFile::CreateFromDirectory
# does exactly the same on Windows -- so the documented fix is not one. The zip spec
# says forward slashes, and only writing the entries by hand guarantees them.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = [System.IO.Compression.ZipFile]::Open($archive, 'Create')
try {
    foreach ($file in Get-ChildItem $stage -Recurse -File) {
        $relative = $file.FullName.Substring($stage.Length).TrimStart([char]92, [char]47)
        $relative = $relative.Replace([char]92, [char]47)

        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative) | Out-Null
    }
}
finally {
    $zip.Dispose()
}

Write-Host "Packed $archive" -ForegroundColor Green

if ($InstallPath) {
    if (-not (Test-Path $InstallPath)) { throw "No such install: $InstallPath" }

    $target = Join-Path $InstallPath 'user/mods/Poker'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $modFolder '*') -Destination $target -Force

    Write-Host "Installed to $target" -ForegroundColor Green
}

Write-Host ''
Write-Host 'Extract the zip at the root of the SPT install, then start the server.' -ForegroundColor Cyan
Write-Host 'Look for a [Poker] block in the console -- silence means the version gate.'
