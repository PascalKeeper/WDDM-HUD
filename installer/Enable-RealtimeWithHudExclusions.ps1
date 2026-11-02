#Requires -RunAsAdministrator
# WDDM HUD by pascalkeeper
# Keeps real-time protection on. Excludes only this HUD's driver files and exe.
# Does not change Tamper Protection, the vulnerable-driver blocklist, or other Defender features.

$ErrorActionPreference = 'Stop'
$InstallRoot = 'F:\imonlinegaming\WDDM_HUD_by_pascalkeeper'
$Dist = Join-Path $InstallRoot 'dist'
$Log = Join-Path $InstallRoot 'logs\realtime-protection.log'
New-Item -ItemType Directory -Force -Path (Join-Path $InstallRoot 'logs') | Out-Null

function Write-Log([string]$Message) {
    $line = '{0} {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    Add-Content -Path $Log -Value $line -Encoding utf8
    Write-Host $line
}

$paths = @(
    (Join-Path $Dist 'WddmHud.sys'),
    (Join-Path $Dist 'WinRing0.sys'),
    (Join-Path $Dist 'WinRing0x64.sys')
)
$process = Join-Path $Dist 'WddmHud.exe'

$pref = Get-MpPreference
foreach ($path in $paths) {
    if ($pref.ExclusionPath -contains $path) {
        Write-Log "Path exclusion already present: $path"
    } else {
        Add-MpPreference -ExclusionPath $path
        Write-Log "Added path exclusion: $path"
    }
}
if ($pref.ExclusionProcess -contains $process) {
    Write-Log "Process exclusion already present: $process"
} else {
    Add-MpPreference -ExclusionProcess $process
    Write-Log "Added process exclusion: $process"
}

Set-MpPreference -DisableRealtimeMonitoring $false
Write-Log 'Real-time protection set on.'

$status = $null
foreach ($try in 1..8) {
    Start-Sleep -Seconds 1
    $status = Get-MpComputerStatus
    if ($status.RealTimeProtectionEnabled) { break }
}
$pref = Get-MpPreference
Write-Log ("RTP={0} Tamper={1} RebootRequired={2}" -f $status.RealTimeProtectionEnabled, $status.IsTamperProtected, $status.RebootRequired)
Write-Log ('ExclusionPath=' + (($pref.ExclusionPath | Where-Object { $_ -like '*WddmHud*' -or $_ -like '*WinRing0*' }) -join ' | '))
Write-Log ('ExclusionProcess=' + (($pref.ExclusionProcess | Where-Object { $_ -like '*WddmHud*' }) -join ' | '))

if (-not $status.RealTimeProtectionEnabled) {
    Write-Log 'Real-time protection did not turn on.'
    exit 1
}
exit 0
