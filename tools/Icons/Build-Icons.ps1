using namespace System.Windows.Media
<#
.SYNOPSIS
    Generates the app's button and status icons from Fluent UI System Icons.

.DESCRIPTION
    Reads the SVGs in tools\Icons\fluent (Fluent UI System Icons by Microsoft, MIT License: see LICENSE there) and
    writes src\ARKServerManager\Resources\Icons.xaml and src\Plugin.Discord\Resources\Icons.xaml: one vector image
    per icon, "Icon.<Name>" (20 x 20) and, where Fluent has a 16 px design, "Icon.<Name>.16" (16 x 16) for places
    that show it at 16 px, plus light "Icon.<Name>.OnDark" versions for the main window's dark header. Use them as
    {StaticResource Icon.Save}, or {DynamicResource ...} in the loose XAML under Styles\.

    To add or change an icon, edit $Icons below (name = Fluent icon name, style, colour) and run the script with
    -Download, which fetches SVGs that aren't in tools\Icons\fluent yet. Browse the icons at
    https://github.com/microsoft/fluentui-system-icons. Don't edit the generated files by hand.

    The app icon (Art\favicon.ico) isn't made here.

.PARAMETER Download
    Fetch missing SVGs from the @fluentui/svg-icons package at $FluentVersion.

.EXAMPLE
    tools\Icons\Build-Icons.ps1 -Download
#>
[CmdletBinding()]
param(
    [switch]$Download
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName PresentationCore, WindowsBase

$FluentVersion = '1.1.347'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$svgDir = Join-Path $PSScriptRoot 'fluent'

# colours, as brushes in the generated dictionary
$Brushes = [ordered]@{
    Neutral = '#FF323130'
    Success = '#FF107C10'
    Danger  = '#FFC42B1C'
    Warning = '#FFE07A00'
    Info    = '#FF0F6CBD'
    Muted   = '#FF8A8886'
    OnDark  = '#FFF3F3F3'
}

# app icon name = Fluent icon, style (regular or filled), colour[, 'trim' = no padding to the 20 x 20 box]
$Icons = [ordered]@{
    Add            = 'add', 'regular', 'Success'
    Cancel         = 'prohibited', 'regular', 'Danger'
    ChangeNotes    = 'document_bullet_list', 'regular', 'Neutral'
    Checked        = 'checkbox_checked', 'regular', 'Neutral'
    Close          = 'dismiss', 'filled', 'Neutral', 'trim'
    Command        = 'window_console', 'regular', 'Neutral'
    Copy           = 'copy', 'regular', 'Neutral'
    Delete         = 'delete', 'regular', 'Danger'
    Document       = 'database', 'regular', 'Neutral'
    Down           = 'arrow_down', 'regular', 'Neutral'
    Download       = 'arrow_download', 'regular', 'Neutral'
    Drag           = 're_order_dots_vertical', 'regular', 'Neutral'
    DropArrow      = 'caret_down', 'filled', 'Neutral', 'trim'
    Edit           = 'edit', 'regular', 'Neutral'
    Filter         = 'filter', 'regular', 'Neutral'
    Find           = 'search', 'regular', 'Neutral'
    FolderExport   = 'arrow_export_ltr', 'regular', 'Neutral'
    FolderImport   = 'arrow_import', 'regular', 'Neutral'
    FolderOpen     = 'folder_open', 'regular', 'Neutral'
    Help           = 'question_circle', 'regular', 'Info'
    Logs           = 'folder_list', 'regular', 'Neutral'
    Paste          = 'clipboard_paste', 'regular', 'Neutral'
    Players        = 'people', 'regular', 'Neutral'
    Plugin         = 'puzzle_piece', 'regular', 'Neutral'
    Refresh        = 'arrow_reset', 'regular', 'Neutral'
    Reload         = 'arrow_clockwise', 'regular', 'Neutral'
    Restart        = 'arrow_sync_circle', 'filled', 'Warning'
    Save           = 'save', 'regular', 'Neutral'
    Servers        = 'server_multiple', 'regular', 'Neutral'
    Settings       = 'settings', 'regular', 'Neutral'
    Shortcut       = 'desktop_arrow_right', 'regular', 'Neutral'
    Start          = 'play_circle', 'filled', 'Success'
    StatusBad      = 'dismiss_circle', 'filled', 'Danger'
    StatusGood     = 'checkmark_circle', 'filled', 'Success'
    StatusOff      = 'circle', 'filled', 'Danger'
    StatusOn       = 'circle', 'filled', 'Success'
    StatusStarting = 'circle', 'filled', 'Warning'
    StatusUnknown  = 'circle', 'filled', 'Muted'
    StatusWarning  = 'warning', 'filled', 'Warning'
    Steam          = 'cloud_arrow_down', 'regular', 'Neutral'
    Stop           = 'record_stop', 'filled', 'Danger'
    Unchecked      = 'checkbox_unchecked', 'regular', 'Neutral'
    Up             = 'arrow_up', 'regular', 'Neutral'
    Validate       = 'checkmark', 'regular', 'Success'
    Website        = 'globe', 'regular', 'Neutral'
    Zip            = 'folder_zip', 'regular', 'Neutral'
}

# also drawn light, as "Icon.<Name>.OnDark", for the main window's dark header
$OnDarkIcons = 'ChangeNotes', 'Command', 'Document', 'Help', 'Logs', 'Plugin', 'Settings', 'Steam'

$PluginIcons = 'Add', 'Delete', 'Drag', 'Edit'

function Get-SvgFile([string]$glyph, [int]$size, [string]$style) {
    Join-Path $svgDir "${glyph}_${size}_${style}.svg"
}

function Get-FluentFile([string]$file) {
    Invoke-WebRequest "https://cdn.jsdelivr.net/npm/@fluentui/svg-icons@$FluentVersion/icons/$(Split-Path -Leaf $file)" -OutFile $file
}

# the 20 px designs are required
$missing = @($Icons.Values | ForEach-Object { Get-SvgFile $_[0] 20 $_[1] } | Select-Object -Unique | Where-Object { -not (Test-Path $_) })
if ($missing) {
    if (-not $Download) { throw "Missing SVGs (run with -Download): $(($missing | Split-Path -Leaf) -join ', ')" }
    New-Item -ItemType Directory -Force $svgDir | Out-Null
    foreach ($file in $missing) {
        Write-Host "Downloading $(Split-Path -Leaf $file)"
        Get-FluentFile $file
    }
}
# the 16 px designs are optional: fetch the ones Fluent has
if ($Download) {
    foreach ($entry in $Icons.Values) {
        $file = Get-SvgFile $entry[0] 16 $entry[1]
        if (Test-Path $file) { continue }
        try {
            Get-FluentFile $file
            Write-Host "Downloaded $(Split-Path -Leaf $file)"
        } catch {
            if (Test-Path $file) { Remove-Item $file }
        }
    }
}

# path data of an SVG: Fluent icons are plain paths with the default (nonzero) fill rule
function Get-PathData([string]$file) {
    [xml]$svg = Get-Content -Raw $file
    $paths = @($svg.SelectNodes("//*[local-name()='path']") | ForEach-Object { $_.GetAttribute('d') })
    if (-not $paths) { throw "No paths in $file" }
    $data = 'F1 ' + ($paths -join ' ')
    [void][Geometry]::Parse($data)   # fails here, not at build time, if WPF can't read it
    $data
}

function Add-DrawingImage([System.Text.StringBuilder]$sb, [string]$key, [string]$svg, [string]$comment, [string]$brush, [bool]$trim, [int]$size) {
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("    <!-- $comment -->")
    [void]$sb.AppendLine("    <DrawingImage x:Key=""$key"" po:Freeze=""True"">")
    [void]$sb.AppendLine('        <DrawingImage.Drawing>')
    [void]$sb.AppendLine('            <DrawingGroup>')
    if (-not $trim) {
        # an invisible square, so every icon is the full size and lines up the same way
        [void]$sb.AppendLine("                <GeometryDrawing Brush=""Transparent"" Geometry=""M0,0H${size}V${size}H0Z""/>")
    }
    [void]$sb.AppendLine("                <GeometryDrawing Brush=""{StaticResource Icon${brush}Brush}"" Geometry=""$(Get-PathData $svg)""/>")
    [void]$sb.AppendLine('            </DrawingGroup>')
    [void]$sb.AppendLine('        </DrawingImage.Drawing>')
    [void]$sb.AppendLine('    </DrawingImage>')
}

function Write-IconDictionary([string]$file, [string[]]$names, [string[]]$onDarkNames) {
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('<!--')
    [void]$sb.AppendLine('    Generated by tools\Icons\Build-Icons.ps1 from Fluent UI System Icons ' + $FluentVersion + ' (Microsoft, MIT License).')
    [void]$sb.AppendLine('    Don''t edit by hand: change the script and run it again.')
    [void]$sb.AppendLine('-->')
    [void]$sb.AppendLine('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"')
    [void]$sb.AppendLine('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"')
    [void]$sb.AppendLine('                    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"')
    [void]$sb.AppendLine('                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options"')
    [void]$sb.AppendLine('                    mc:Ignorable="po">')
    foreach ($b in $Brushes.GetEnumerator()) {
        [void]$sb.AppendLine("    <SolidColorBrush x:Key=""Icon$($b.Key)Brush"" Color=""$($b.Value)"" po:Freeze=""True""/>")
    }
    foreach ($name in $names) {
        $entry = $Icons[$name]
        $glyph, $style, $brush = $entry[0..2]
        $trim = $entry.Count -gt 3 -and $entry[3] -eq 'trim'
        foreach ($size in 20, 16) {
            $svg = Get-SvgFile $glyph $size $style
            if (-not (Test-Path $svg)) { continue }
            $key = if ($size -eq 20) { "Icon.$name" } else { "Icon.$name.16" }
            Add-DrawingImage $sb $key $svg "$glyph, $style" $brush $trim $size
        }
    }
    foreach ($name in $onDarkNames) {
        $entry = $Icons[$name]
        $trim = $entry.Count -gt 3 -and $entry[3] -eq 'trim'
        Add-DrawingImage $sb "Icon.$name.OnDark" (Get-SvgFile $entry[0] 20 $entry[1]) "$($entry[0]), $($entry[1]), on a dark background" 'OnDark' $trim 20
    }
    [void]$sb.AppendLine('</ResourceDictionary>')
    New-Item -ItemType Directory -Force (Split-Path $file) | Out-Null
    [IO.File]::WriteAllText($file, $sb.ToString().Replace("`r`n", "`n").Replace("`n", "`r`n"), [Text.UTF8Encoding]::new($true))
    Write-Host "Wrote $($file.Substring($repo.Length + 1)) ($($names.Count) icons)"
}

Write-IconDictionary (Join-Path $repo 'src\ARKServerManager\Resources\Icons.xaml') @($Icons.Keys) $OnDarkIcons
Write-IconDictionary (Join-Path $repo 'src\Plugin.Discord\Resources\Icons.xaml') $PluginIcons @()
