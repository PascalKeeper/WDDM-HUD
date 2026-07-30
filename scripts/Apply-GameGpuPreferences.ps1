#Requires -Version 5.1
<#
.SYNOPSIS
    Force high-performance dGPU for Fortnite, RDR2, WRC9 via UserGpuPreferences.
    Cleans corrupt truncated keys (C:\Program, E:\Program). Aligns with Mother Earth.
    Does NOT change power plans.
#>
[CmdletBinding()]
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'
$RegPath = 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences'
$HighPerf = 'GpuPreference=2;'
$PowerSaving = 'GpuPreference=1;'

function Write-Log([string]$m, [string]$l='INFO') {
    $c = switch ($l) { 'OK'{'Green'} 'WARN'{'Yellow'} 'ERROR'{'Red'} 'DRY'{'DarkYellow'} default{'Cyan'} }
    Write-Host ("[{0}] [{1}] {2}" -f (Get-Date -Format 'HH:mm:ss'), $l, $m) -ForegroundColor $c
}

function Set-GpuPref {
    param([string]$ExePath, [string]$Value = $HighPerf)
    if ([string]::IsNullOrWhiteSpace($ExePath)) { return }
    if (-not (Test-Path -LiteralPath $ExePath)) {
        Write-Log "Skip (not found): $ExePath" 'WARN'
        return
    }
    # Full path only — NEVER write truncated fragments
    if ($ExePath -notmatch '\\[^\\]+\.exe$') {
        Write-Log "Reject non-exe path: $ExePath" 'ERROR'
        return
    }
    if ($DryRun) {
        Write-Log "Would set $ExePath = $Value" 'DRY'
        return
    }
    if (-not (Test-Path $RegPath)) {
        New-Item -Path $RegPath -Force | Out-Null
    }
    New-ItemProperty -Path $RegPath -Name $ExePath -Value $Value -PropertyType String -Force | Out-Null
    Write-Log "Set $Value -> $ExePath" 'OK'
}

function Remove-CorruptKeys {
    if (-not (Test-Path $RegPath)) { return }
    $props = Get-ItemProperty -Path $RegPath
    $bad = @()
    foreach ($p in $props.PSObject.Properties) {
        $n = $p.Name
        if ($n -in @('PSPath','PSParentPath','PSChildName','PSDrive','PSProvider')) { continue }
        # Truncated path remnants from prior tooling bugs
        if ($n -eq 'C:\Program' -or $n -eq 'E:\Program' -or $n -eq 'D:\Program' -or $n -eq 'F:\Program') {
            $bad += $n
        }
        # Incomplete path that is not a real exe
        elseif ($n -match '^[A-Z]:\\Program$' ) {
            $bad += $n
        }
    }
    foreach ($b in $bad) {
        if ($DryRun) { Write-Log "Would remove corrupt key: $b" 'DRY' }
        else {
            Remove-ItemProperty -Path $RegPath -Name $b -Force -ErrorAction SilentlyContinue
            Write-Log "Removed corrupt key: $b" 'OK'
        }
    }
}

# ---- Target discovery ----
$targets = New-Object System.Collections.Generic.List[string]

# Fortnite (Epic)
@(
    'C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping.exe',
    'C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteLauncher.exe',
    'C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteClient-Win64-Shipping_EAC_EOS.exe',
    'C:\Program Files\Epic Games\Fortnite\FortniteGame\Binaries\Win64\FortniteBootstrapper.exe',
    'C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe'
) | ForEach-Object { if (Test-Path $_) { $targets.Add($_) } }

# RDR2 (Epic)
@(
    'C:\Program Files\Epic Games\RedDeadRedemption2\RDR2.exe',
    'C:\Program Files\Epic Games\RedDeadRedemption2\PlayRDR2.exe'
) | ForEach-Object { if (Test-Path $_) { $targets.Add($_) } }

# WRC 9
@(
    'E:\Program Files\WRC9\WRC9\WRC9.exe',
    'E:\Program Files\WRC9\WRC9.exe'
) | ForEach-Object { if (Test-Path $_) { $targets.Add($_) } }

# Shell / background → iGPU (Mother Earth aligned)
$igpu = @(
    "$env:WINDIR\explorer.exe"
)
foreach ($sys in @('SearchApp.exe','ShellExperienceHost.exe','TextInputHost.exe','RuntimeBroker.exe','LockApp.exe')) {
    $p1 = Join-Path $env:WINDIR "System32\$sys"
    $p2 = Join-Path $env:WINDIR "SystemApps"
    if (Test-Path $p1) { $igpu += $p1 }
}

Write-Log "DryRun=$DryRun" 'INFO'
Remove-CorruptKeys

foreach ($t in ($targets | Select-Object -Unique)) {
    Set-GpuPref -ExePath $t -Value $HighPerf
}

foreach ($t in ($igpu | Select-Object -Unique)) {
    if (Test-Path $t) {
        # explorer on iGPU saves dGPU for games
        if ($DryRun) { Write-Log "Would set iGPU pref: $t" 'DRY' }
        else {
            if (-not (Test-Path $RegPath)) { New-Item -Path $RegPath -Force | Out-Null }
            New-ItemProperty -Path $RegPath -Name $t -Value $PowerSaving -PropertyType String -Force | Out-Null
            Write-Log "Set $PowerSaving -> $t" 'OK'
        }
    }
}

Write-Log 'Game GPU preference pass complete.' 'OK'
if ($targets.Count -eq 0) {
    Write-Log 'No target game executables were found on disk.' 'WARN'
}
