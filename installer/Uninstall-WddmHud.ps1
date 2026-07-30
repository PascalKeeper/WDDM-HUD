#Requires -Version 5.1
<#
.SYNOPSIS
    Clean uninstall for WDDM HUD by pascalkeeper.
    Stops process, removes shortcuts, HKCU uninstall key. Does not touch Mother Earth.
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$KeepConfig,
    [switch]$KeepLogs
)

$ErrorActionPreference = 'Stop'
$InstallRoot = 'F:\imonlinegaming\WDDM_HUD_by_pascalkeeper'
$ProductName = 'WDDM HUD by pascalkeeper'

function Write-Log([string]$m, [string]$l='INFO') {
    $c = switch ($l) { 'OK'{'Green'} 'WARN'{'Yellow'} 'ERROR'{'Red'} 'DRY'{'DarkYellow'} default{'Cyan'} }
    Write-Host ("[{0}] [{1}] {2}" -f (Get-Date -Format 'HH:mm:ss'), $l, $m) -ForegroundColor $c
}

try {
    Write-Log "Uninstall starting (DryRun=$DryRun)" 'INFO'

    # Stop running HUD (UI + sensor-bridge) and purge ghosts
    $ghost = Join-Path $InstallRoot 'scripts\Stop-WddmHudGhosts.ps1'
    if (Test-Path $ghost) {
        if ($DryRun) {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ghost -DryRun
        } else {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ghost
        }
    } else {
        Get-Process -Name 'WddmHud' -ErrorAction SilentlyContinue | ForEach-Object {
            if ($DryRun) { Write-Log "Would stop PID $($_.Id)" 'DRY' }
            else {
                Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
                Write-Log "Stopped PID $($_.Id)" 'OK'
            }
        }
    }

    # Shortcuts
    $paths = @(
        (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WDDM HUD by pascalkeeper'),
        (Join-Path ([Environment]::GetFolderPath('Desktop')) 'WDDM HUD.lnk')
    )
    foreach ($p in $paths) {
        if (Test-Path $p) {
            if ($DryRun) { Write-Log "Would remove $p" 'DRY' }
            else { Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue; Write-Log "Removed $p" 'OK' }
        }
    }

    # Uninstall registry
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WddmHud_by_pascalkeeper'
    if (Test-Path $key) {
        if ($DryRun) { Write-Log "Would remove $key" 'DRY' }
        else { Remove-Item $key -Recurse -Force; Write-Log 'Removed uninstall registry key' 'OK' }
    }

    # Optional config/logs retention
    if (-not $KeepConfig) {
        $cfg = Join-Path $InstallRoot 'config'
        if (Test-Path $cfg) {
            if ($DryRun) { Write-Log "Would remove $cfg" 'DRY' }
            else { Remove-Item $cfg -Recurse -Force -ErrorAction SilentlyContinue; Write-Log 'Removed config' 'OK' }
        }
    }
    if (-not $KeepLogs) {
        $logs = Join-Path $InstallRoot 'logs'
        if (Test-Path $logs) {
            if ($DryRun) { Write-Log "Would remove $logs" 'DRY' }
            else { Remove-Item $logs -Recurse -Force -ErrorAction SilentlyContinue; Write-Log 'Removed logs' 'OK' }
        }
    }

    # Dist exe (source tree left intact for developers)
    $exe = Join-Path $InstallRoot 'dist\WddmHud.exe'
    if (Test-Path $exe) {
        if ($DryRun) { Write-Log "Would remove $exe" 'DRY' }
        else { Remove-Item $exe -Force -ErrorAction SilentlyContinue; Write-Log 'Removed dist exe' 'OK' }
    }

    Write-Log 'Uninstall complete. Source tree preserved under install root.' 'OK'
    Write-Log 'NOTE: Game GpuPreference entries and power plan cleanup are NOT auto-reverted.' 'WARN'
    Write-Log 'Use EmergencyRestore.bat / Mother Earth --restore if you need system-level revert.' 'WARN'
    exit 0
}
catch {
    Write-Log $_.Exception.Message 'ERROR'
    exit 1
}
