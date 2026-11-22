<#
.SYNOPSIS
    Copies the Release build to src\ARKServerManager\bin\UiTest\app as an isolated test copy for UiDriver.

.DESCRIPTION
    The copy gets its own settings (Windows keys them by the exe's folder) and its own data folder,
    src\ARKServerManager\bin\UiTest\data, so it never sees the real profiles or servers.

    On its first start the app migrates userconfig.json / commonconfig.json from its install folder (its settings
    backup). The real ones are left out of the copy, and a seed userconfig.json sets the data folder and turns off
    the "run as administrator" and "already running" prompts: an elevated copy can't be driven by a normal
    process, and the prompts would otherwise sit on the desktop waiting for someone to answer.

.PARAMETER Reset
    Delete the copy's settings and data folder first, so it starts like a new installation.
#>
param([switch]$Reset)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$release = Join-Path $repo 'src\ARKServerManager\bin\Release\net11.0-windows'
$root = Join-Path $repo 'src\ARKServerManager\bin\UiTest'
$app = Join-Path $root 'app'
$data = Join-Path $root 'data'
$exeName = 'JVH Ark Ascended Server Manager.exe'

if (-not (Test-Path (Join-Path $release $exeName))) {
    throw "No Release build at $release; build it first (dotnet build src\ARKServerManager -c Release)."
}

# the test copy can't be replaced while it runs (an elevated copy hides its path, so check the command line too)
$running = Get-CimInstance Win32_Process -Filter "Name = '$exeName'" | Where-Object {
    ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($app, [StringComparison]::OrdinalIgnoreCase)) -or
    ($_.CommandLine -and $_.CommandLine.IndexOf($app, [StringComparison]::OrdinalIgnoreCase) -ge 0)
}
if ($running) {
    throw "The test copy is running (PID $($running.ProcessId -join ', ')); close it first (UiDriver close, or UiDriver kill)."
}

if ($Reset) {
    # the copy's settings folder is the one whose user.config points at the test data folder
    $pattern = '<value>' + [regex]::Escape($data) + '\\?</value>'
    Get-ChildItem (Join-Path $env:LOCALAPPDATA 'JVH') -Directory -ErrorAction SilentlyContinue | Where-Object {
        Get-ChildItem $_.FullName -Recurse -Filter user.config | Where-Object { (Get-Content $_.FullName -Raw) -match $pattern }
    } | ForEach-Object {
        Remove-Item $_.FullName -Recurse -Force
        Write-Output "Removed settings $($_.FullName)"
    }

    if (Test-Path $data) {
        Remove-Item $data -Recurse -Force
        Write-Output "Removed $data"
    }
}

robocopy $release $app /MIR /XF userconfig.json commonconfig.json /NJH /NJS /NP /NFL /NDL | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "robocopy failed (exit code $LASTEXITCODE)."
}

New-Item -ItemType Directory -Force $data | Out-Null
[ordered]@{
    DataDir                              = $data
    RunAsAdministratorPrompt             = $false
    CheckIfServerManagerRunningOnStartup = $false
    ManageFirewallAutomatically          = $false
} | ConvertTo-Json | Set-Content (Join-Path $app 'userconfig.json') -Encoding UTF8
Remove-Item (Join-Path $app 'commonconfig.json') -ErrorAction SilentlyContinue

Write-Output "Test copy: $(Join-Path $app $exeName)"
Write-Output "Data folder: $data"
