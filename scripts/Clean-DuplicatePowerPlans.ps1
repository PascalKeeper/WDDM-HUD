#Requires -Version 5.1
<#
.SYNOPSIS
    Delete duplicate power plan remnants (Ultimate Performance / Rocco Infinity clones).
    NEVER changes the active power plan.
    NEVER deletes High Performance / Balanced stock plans.
    NEVER deletes Rocco Infinity unless duplicates exist (keeps one of each name group).

.NOTES
    Project coordinator decision: do not switch to Ultimate Performance.
    This script only removes clutter from past experiments.
#>
[CmdletBinding()]
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'

function Write-Log([string]$m, [string]$l='INFO') {
    $c = switch ($l) { 'OK'{'Green'} 'WARN'{'Yellow'} 'ERROR'{'Red'} 'DRY'{'DarkYellow'} default{'Cyan'} }
    Write-Host ("[{0}] [{1}] {2}" -f (Get-Date -Format 'HH:mm:ss'), $l, $m) -ForegroundColor $c
}

function Test-IsAdmin {
    $p = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Stock plans that must never be deleted
$ProtectedGuids = @(
    '381b4222-f694-41f0-9685-ff5bb260df2e', # Balanced
    '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c', # High performance
    'a1841308-3541-4fab-bc81-f71556f20b4a'  # Power saver
)

if (-not $DryRun -and -not (Test-IsAdmin)) {
    Write-Log 'Administrator required to delete power schemes. Re-run elevated or use installer option.' 'ERROR'
    exit 2
}

# Parse powercfg /list
$list = powercfg /list 2>&1 | Out-String
$activeGuid = $null
if ($list -match '\*\s*\r?\n?\s*Power Scheme GUID:\s*([0-9a-fA-F-]+)') {
    # fallback parse line by line
}
$plans = @()
foreach ($line in ($list -split "`r?`n")) {
    if ($line -match 'Power Scheme GUID:\s*([0-9a-fA-F-]+)\s+\((.+)\)\s*(\*)?') {
        $guid = $Matches[1].ToLowerInvariant()
        $name = $Matches[2].Trim()
        $isActive = $line.Contains('*')
        if ($isActive) { $activeGuid = $guid }
        $plans += [pscustomobject]@{ Guid = $guid; Name = $name; Active = $isActive }
    }
}

if (-not $activeGuid) {
    $activeLine = powercfg /getactivescheme 2>&1 | Out-String
    if ($activeLine -match '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})') {
        $activeGuid = $Matches[1].ToLowerInvariant()
    }
}

Write-Log ("Active plan GUID: {0}" -f $activeGuid) 'INFO'
Write-Log ("Discovered {0} power schemes" -f $plans.Count) 'INFO'

# Group by exact name; keep one (prefer active, else first), delete rest
$toDelete = New-Object System.Collections.Generic.List[string]
$groups = $plans | Group-Object -Property Name

foreach ($g in $groups) {
    $name = $g.Name
    $items = @($g.Group)

    # Only clean known clutter families
    $isClutterFamily =
        ($name -eq 'Ultimate Performance') -or
        ($name -eq 'Rocco Infinity Mode') -or
        ($name -like 'Ultimate Performance*') -or
        ($name -like 'Rocco Infinity*')

    if (-not $isClutterFamily) {
        Write-Log "Keep (not clutter family): $name ($($items.Count))" 'INFO'
        continue
    }

    if ($items.Count -le 1) {
        Write-Log "Keep single instance: $name [$($items[0].Guid)]" 'OK'
        continue
    }

    # Prefer keeping the active one if it's in this group; else keep first
    $keep = $items | Where-Object { $_.Active -or $_.Guid -eq $activeGuid } | Select-Object -First 1
    if (-not $keep) { $keep = $items | Select-Object -First 1 }

    Write-Log "Keeping one '$name': $($keep.Guid)" 'OK'
    foreach ($item in $items) {
        if ($item.Guid -eq $keep.Guid) { continue }
        if ($item.Guid -eq $activeGuid) {
            Write-Log "Skip delete ACTIVE: $($item.Guid)" 'WARN'
            continue
        }
        if ($ProtectedGuids -contains $item.Guid) {
            Write-Log "Skip protected stock GUID: $($item.Guid)" 'WARN'
            continue
        }
        $toDelete.Add($item.Guid) | Out-Null
    }
}

if ($toDelete.Count -eq 0) {
    Write-Log 'No duplicate power plans to remove.' 'OK'
    exit 0
}

foreach ($guid in ($toDelete | Select-Object -Unique)) {
    if ($DryRun) {
        Write-Log "Would delete scheme $guid" 'DRY'
        continue
    }
    $out = powercfg /delete $guid 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        Write-Log "Deleted scheme $guid" 'OK'
    } else {
        Write-Log "Failed delete $guid : $out" 'WARN'
    }
}

Write-Log 'Duplicate power plan cleanup complete. Active plan unchanged.' 'OK'
powercfg /getactivescheme | ForEach-Object { Write-Log $_ 'INFO' }
exit 0
