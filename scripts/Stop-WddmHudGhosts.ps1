#Requires -Version 5.1
<#
.SYNOPSIS
    Kill all WDDM HUD UI + sensor-bridge processes and purge old-version remnants.
    Safe to run anytime (install, upgrade, emergency).
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$AlsoRemoveBuildRemnants
)

$ErrorActionPreference = 'Continue'
$Root = 'F:\imonlinegaming\WDDM_HUD_by_pascalkeeper'
$DistExe = Join-Path $Root 'dist\WddmHud.exe'

function Write-Log([string]$m, [string]$l = 'INFO') {
    $c = switch ($l) { 'OK' { 'Green' } 'WARN' { 'Yellow' } 'ERROR' { 'Red' } 'DRY' { 'DarkYellow' } default { 'Cyan' } }
    Write-Host ("[{0}] [{1}] {2}" -f (Get-Date -Format 'HH:mm:ss'), $l, $m) -ForegroundColor $c
}

Write-Log "Stop-WddmHudGhosts starting (DryRun=$DryRun)" 'INFO'

# 1) Prefer official --kill-all on current dist binary
if (Test-Path $DistExe) {
    if ($DryRun) {
        Write-Log "Would run: $DistExe --kill-all" 'DRY'
    } else {
        try {
            $p = Start-Process -FilePath $DistExe -ArgumentList '--kill-all' -WorkingDirectory (Split-Path $DistExe) -PassThru -WindowStyle Hidden
            $p.WaitForExit(15000) | Out-Null
            Write-Log "WddmHud.exe --kill-all exit=$($p.ExitCode)" 'OK'
        } catch {
            Write-Log "kill-all via exe failed: $($_.Exception.Message)" 'WARN'
        }
    }
}

# 2) Hard kill any remaining WddmHud.exe (any path / old version)
$procs = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq 'WddmHud.exe' }
foreach ($pr in $procs) {
    if ($DryRun) {
        Write-Log "Would kill PID $($pr.ProcessId) path=$($pr.ExecutablePath) cmd=$($pr.CommandLine)" 'DRY'
    } else {
        try {
            Stop-Process -Id $pr.ProcessId -Force -ErrorAction Stop
            Write-Log "Killed PID $($pr.ProcessId) ($($pr.ExecutablePath))" 'OK'
        } catch {
            Write-Log "Kill PID $($pr.ProcessId) failed: $($_.Exception.Message)" 'WARN'
        }
    }
}

Start-Sleep -Milliseconds 400

# 3) Purge leftover driver droppings + stale sensor/state files
$purge = @(
    (Join-Path $Root 'dist\*.sys'),
    (Join-Path $Root 'config\live_sensors.json'),
    (Join-Path $Root 'config\run_state_ui.json'),
    (Join-Path $Root 'config\run_state_bridge.json')
)
foreach ($pattern in $purge) {
    Get-Item $pattern -ErrorAction SilentlyContinue | ForEach-Object {
        if ($DryRun) { Write-Log "Would delete $($_.FullName)" 'DRY' }
        else {
            Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
            Write-Log "Deleted $($_.FullName)" 'OK'
        }
    }
}

# 4) Optional: remove non-ship build outputs that can be mistaken for the app
if ($AlsoRemoveBuildRemnants) {
    $buildExes = @(
        (Join-Path $Root 'src\WddmHud\bin\Debug\net8.0-windows\win-x64\WddmHud.exe'),
        (Join-Path $Root 'src\WddmHud\bin\Release\net8.0-windows\win-x64\WddmHud.exe')
    )
    foreach ($b in $buildExes) {
        if (Test-Path $b) {
            if ($DryRun) { Write-Log "Would delete build remnant $b" 'DRY' }
            else {
                Remove-Item $b -Force -ErrorAction SilentlyContinue
                Write-Log "Deleted build remnant $b" 'OK'
            }
        }
    }
}

# 5) Report remaining
$left = @(Get-Process -Name 'WddmHud' -ErrorAction SilentlyContinue)
if ($left.Count -eq 0) {
    Write-Log 'No WddmHud processes remain.' 'OK'
} else {
    Write-Log ("STILL RUNNING: " + (($left | ForEach-Object { $_.Id }) -join ', ')) 'ERROR'
    exit 1
}

Write-Log 'Ghost cleanup complete. Launch only dist\WddmHud.exe (or Desktop shortcut).' 'OK'
exit 0
