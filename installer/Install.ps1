<#
.SYNOPSIS
    Installs (or removes) the Turn Telemetry plugin and the Turn Telemetry Dashboard into SimHub.

.DESCRIPTION
    Run it through Install.cmd / Uninstall.cmd (they bypass the PowerShell execution policy for this script only).
    Finds SimHub from the registry or the default folder, waits for SimHub to close, copies the plugin DLLs into the
    SimHub folder and the dashboard into SimHub\DashTemplates. Asks for administrator rights only when the SimHub
    folder isn't writable. Your turn edits and learned sectors (SimHub\PluginsData\TurnTelemetry) are never touched.

.PARAMETER SimHubDir
    SimHub folder, when it isn't found automatically.

.PARAMETER Uninstall
    Remove the plugin DLLs and the dashboard instead.
#>
param(
    [string]$SimHubDir,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$pluginFiles = @('TurnTelemetry.dll', 'TurnTelemetry.Core.dll')
$dashName = 'TurnTelemetryDashboard'

function Find-SimHub {
    if ($SimHubDir) { return $SimHubDir.TrimEnd('\') }
    $keys = @('HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
              'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
              'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')
    foreach ($key in $keys) {
        foreach ($entry in @(Get-ItemProperty $key -ErrorAction SilentlyContinue)) {
            if ($entry.DisplayName -like 'SimHub*' -and $entry.InstallLocation) {
                $dir = $entry.InstallLocation.Trim('"').TrimEnd('\')
                if (Test-Path (Join-Path $dir 'SimHubWPF.exe')) { return $dir }
            }
        }
    }
    foreach ($dir in @("${env:ProgramFiles(x86)}\SimHub", "$env:ProgramFiles\SimHub")) {
        if (Test-Path (Join-Path $dir 'SimHubWPF.exe')) { return $dir }
    }
    return $null
}

function Test-Writable([string]$dir) {
    $probe = Join-Path $dir ('.turntelemetry-write-test-' + [guid]::NewGuid())
    try { [IO.File]::WriteAllText($probe, 'x'); Remove-Item $probe -Force; return $true } catch { return $false }
}

function Wait-SimHubClosed {
    if (-not (Get-Process SimHubWPF -ErrorAction SilentlyContinue)) { return }
    Write-Host ''
    Write-Host 'SimHub is running. It locks plugin files, so it must be closed first.' -ForegroundColor Yellow
    $answer = Read-Host 'Close SimHub now? [Y/n]'
    if ($answer -eq '' -or $answer -match '^[Yy]') {
        Get-Process SimHubWPF -ErrorAction SilentlyContinue | ForEach-Object { [void]$_.CloseMainWindow() }
    } else {
        Write-Host 'Close SimHub yourself (also from the system tray); waiting...'
    }
    while (Get-Process SimHubWPF -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }
    Start-Sleep -Seconds 1
}

function Expand-Dash([string]$package, [string]$templates) {
    # .simhubdash is a zip holding the dashboard folder; Expand-Archive refuses non-.zip names on Windows PowerShell.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        foreach ($entry in $zip.Entries) {
            if (-not $entry.Name) { continue }
            $target = Join-Path $templates ($entry.FullName -replace '/', '\')
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    } finally { $zip.Dispose() }
}

Write-Host ''
Write-Host '=== Turn Telemetry for SimHub ===' -ForegroundColor Cyan

$simhub = Find-SimHub
if (-not $simhub) {
    Write-Host 'SimHub was not found. Install SimHub first, or run:' -ForegroundColor Red
    Write-Host '  Install.cmd -SimHubDir "D:\path\to\SimHub"'
    exit 1
}
Write-Host "SimHub folder: $simhub"

if (-not (Test-Writable $simhub)) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) { Write-Host "Can't write to $simhub even as administrator." -ForegroundColor Red; exit 1 }
    Write-Host 'The SimHub folder needs administrator rights; asking Windows for permission...'
    $relaunch = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($MyInvocation.MyCommand.Path)`"", '-SimHubDir', "`"$simhub`"")
    if ($Uninstall) { $relaunch += '-Uninstall' }
    Start-Process powershell -Verb RunAs -ArgumentList $relaunch -Wait
    exit 0
}

Wait-SimHubClosed
$templates = Join-Path $simhub 'DashTemplates'

if ($Uninstall) {
    foreach ($f in $pluginFiles + @('TurnTelemetry.pdb', 'TurnTelemetry.Core.pdb')) {
        $path = Join-Path $simhub $f
        if (Test-Path $path) { Remove-Item $path -Force; Write-Host "Removed $f" }
    }
    $dash = Join-Path $templates $dashName
    if (Test-Path $dash) { Remove-Item $dash -Recurse -Force; Write-Host "Removed dashboard $dashName" }
    Write-Host ''
    Write-Host 'Turn Telemetry is uninstalled. Your turn edits and learned sectors are kept in' -ForegroundColor Green
    Write-Host "  $simhub\PluginsData\TurnTelemetry  (delete that folder to remove them too)."
    exit 0
}

# Files downloaded from the internet carry a "blocked" mark that stops .NET loading the DLLs.
Get-ChildItem $here -Recurse -File | Unblock-File

foreach ($f in $pluginFiles) {
    Copy-Item (Join-Path $here "plugin\$f") (Join-Path $simhub $f) -Force
    Write-Host "Installed plugin   $f"
}
Expand-Dash (Join-Path $here "$dashName.simhubdash") $templates
Write-Host "Installed dashboard Turn Telemetry Dashboard ($templates\$dashName)"

Write-Host ''
Write-Host 'Done. Next:' -ForegroundColor Green
Write-Host '  1. Start SimHub. When it asks whether to enable "Turn Telemetry", choose Yes.'
Write-Host '  2. Dash Studio: pick "Turn Telemetry Dashboard" for your tablet / second screen'
Write-Host '     (or open http://<this-pc-ip>:8888 on the tablet).'
Write-Host ''
$answer = Read-Host 'Start SimHub now? [Y/n]'
if ($answer -eq '' -or $answer -match '^[Yy]') { Start-Process (Join-Path $simhub 'SimHubWPF.exe') }
