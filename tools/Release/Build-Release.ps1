<#
.SYNOPSIS
    Builds a release zip of JVH Ark Ascended Server Manager.

.DESCRIPTION
    Publishes a clean Release build of the app into an empty staging folder, adds README, CHANGELOG and
    THIRD-PARTY-NOTICES, checks that nothing that mustn't ship is in it, and zips it as
    artifacts\JVH-Ark-Ascended-Server-Manager-<version>.zip (artifacts\ is ignored by git).

    Publishing copies only the app's own files, so a settings backup (userconfig.json / commonconfig.json, which a
    copy of the app that has run writes into its folder) can't get in, even when bin\Release has one.

.PARAMETER Version
    The version to stamp on the assemblies, e.g. 1.1.0. Defaults to <Version> in src\Directory.Build.props.
    Keep it above 1.0 (see the comment there).

.PARAMETER AllowDirty
    Build even when the working tree has uncommitted changes (the zip then doesn't match a commit).

.EXAMPLE
    tools\Release\Build-Release.ps1
    tools\Release\Build-Release.ps1 -Version 1.2.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repo 'src\ARKServerManager\ARKServerManager.csproj'
$exeName = 'JVH Ark Ascended Server Manager.exe'

# version: the parameter, else src\Directory.Build.props
if (-not $Version) {
    [xml]$props = Get-Content -Raw (Join-Path $repo 'src\Directory.Build.props')
    $node = $props.SelectSingleNode('/Project/PropertyGroup/Version')
    if (-not $node) { throw 'No <Version> in src\Directory.Build.props; pass -Version.' }
    $Version = $node.InnerText.Trim()
}
$parsed = $null
if (-not [Version]::TryParse($Version, [ref]$parsed)) { throw "'$Version' isn't a version number like 1.1.0." }
if ($parsed -le [Version]'1.0') { throw "Version $Version must be above 1.0, or the app won't carry users' settings over." }

# the commit being released
Push-Location $repo
try {
    $commit = (git rev-parse --short HEAD).Trim()
    $dirty = [bool](git status --porcelain)
} finally {
    Pop-Location
}
if ($dirty -and -not $AllowDirty) { throw 'The working tree has uncommitted changes. Commit them, or pass -AllowDirty.' }

$artifacts = Join-Path $repo 'artifacts'
$name = "JVH-Ark-Ascended-Server-Manager-$Version"
$staging = Join-Path $artifacts $name
$zip = Join-Path $artifacts "$name.zip"
# a build folder of its own: a clean build every time, and no clash with a copy of the app running from bin\Release
$build = Join-Path $artifacts 'build'

Write-Host "Building $name from $commit$(if ($dirty) { ' (with uncommitted changes)' })"
foreach ($path in $staging, $build) {
    if (Test-Path $path) { Remove-Item -Recurse -Force $path }
}
if (Test-Path $zip) { Remove-Item -Force $zip }
New-Item -ItemType Directory -Force $staging | Out-Null

# framework-dependent: needs the .NET Desktop Runtime, as README says
dotnet publish $project -c Release -o $staging --artifacts-path $build -p:Version=$Version --nologo -v quiet -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }

foreach ($doc in 'README.md', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md') {
    Copy-Item (Join-Path $repo $doc) $staging
}

# checks: the app is there with the right version, and nothing that mustn't ship
$exe = Join-Path $staging $exeName
if (-not (Test-Path $exe)) { throw "$exeName is missing from the build." }
$built = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $staging ($exeName -replace '\.exe$', '.dll'))).Version
if ($built -ne [Version]"$($parsed.Major).$($parsed.Minor).$([Math]::Max($parsed.Build, 0)).$([Math]::Max($parsed.Revision, 0))") {
    throw "The app was built as version $built, not $Version."
}
$forbidden = Get-ChildItem -Recurse -File $staging | Where-Object {
    $_.Name -in 'userconfig.json', 'commonconfig.json' -or
    $_.Name -like 'ServerManagerUpdater*' -or
    $_.Extension -eq '.pdb'
}
if ($forbidden) { throw "These mustn't ship: $($forbidden.FullName -join ', ')" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)

$files = (Get-ChildItem -Recurse -File $staging).Count
$sizeMb = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
Write-Host "Done: $zip"
Write-Host "  $files files, $sizeMb MB, version $built, commit $commit"
Write-Host "  SHA-256 $hash"
