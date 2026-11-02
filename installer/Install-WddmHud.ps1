#Requires -Version 5.1
<#
.SYNOPSIS
    One-click installer for WDDM HUD by pascalkeeper (Windows 10 all builds x64).

.DESCRIPTION
    - Builds self-contained offline WddmHud.exe when SDK is present, or installs prebuilt dist
    - Creates Start Menu + Desktop shortcuts that launch the .EXE ONLY (never "Open with")
    - Optional: apply game GpuPreference for Fortnite / RDR2 / WRC9
    - Optional: clean duplicate power plan remnants (does NOT change active plan)
    - Dry-run mode, emergency restore snapshot, elite error handling
    - Zero telemetry. Preferred root: F:\imonlinegaming\WDDM_HUD_by_pascalkeeper

.PARAMETER DryRun
    Simulate all actions; write nothing permanent except a dry-run log.

.PARAMETER SkipBuild
    Skip dotnet publish; use existing dist\WddmHud.exe if present.

.PARAMETER ApplyGameGpuPrefs
    Register GpuPreference=2 for Fortnite, RDR2, WRC9 (and clean corrupt keys).

.PARAMETER CleanDuplicatePowerPlans
    Delete duplicate Ultimate Performance / Rocco Infinity remnants; never deletes active plan.

.PARAMETER NoShortcuts
    Do not create Start Menu / Desktop shortcuts.

.PARAMETER StartAfterInstall
    Launch the HUD after successful install.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-WddmHud.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-WddmHud.ps1 -DryRun

.NOTES
    Copyright (C) 2026 Joseph Rocco Peransi
    www.github.com/pascalkeeper
    Requirements that a person must confirm are in docs\INSTALLER_NOTES.md.
    This script does not change Defender, the driver blocklist, the power plan,
    the panel refresh rate, or the default audio device.
    Always double-click Install-WddmHud.bat (not the .ps1) for end users — the .bat
    ensures the correct host and avoids the "How do you want to open this file?" dialog.
#>

[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$SkipBuild,
    [switch]$ApplyGameGpuPrefs,
    [switch]$CleanDuplicatePowerPlans,
    [switch]$NoShortcuts,
    [switch]$StartAfterInstall,
    [string]$InstallRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $Script:InstallRoot = 'F:\imonlinegaming\WDDM_HUD_by_pascalkeeper'
} else {
    $Script:InstallRoot = $InstallRoot.TrimEnd('\')
}
$Script:ProductName = 'WDDM HUD by pascalkeeper'
$Script:ExeName     = 'WddmHud.exe'
$Script:LogDir      = Join-Path $InstallRoot 'logs'
$Script:DistDir     = Join-Path $InstallRoot 'dist'
$Script:BackupDir   = Join-Path $InstallRoot 'backups'
$Script:SrcProj     = Join-Path $InstallRoot 'src\WddmHud\WddmHud.csproj'
$Script:Stamp       = Get-Date -Format 'yyyyMMdd-HHmmss'
$Script:LogFile     = Join-Path $LogDir ("install_{0}.log" -f $Stamp)

# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------
function Write-Log {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('INFO','WARN','ERROR','OK','DRY')][string]$Level = 'INFO'
    )
    $line = '[{0}] [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    switch ($Level) {
        'INFO'  { Write-Host $line -ForegroundColor Cyan }
        'WARN'  { Write-Host $line -ForegroundColor Yellow }
        'ERROR' { Write-Host $line -ForegroundColor Red }
        'OK'    { Write-Host $line -ForegroundColor Green }
        'DRY'   { Write-Host $line -ForegroundColor DarkYellow }
    }
    try {
        if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
        Add-Content -Path $LogFile -Value $line -Encoding UTF8
    } catch { }
}

function Test-IsAdministrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p  = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-Windows10Plus {
    $v = [Environment]::OSVersion.Version
    if ($v.Major -lt 10) {
        throw "Windows 10 or newer is required. Detected: $($v.ToString())"
    }
    Write-Log "OS Version OK: $($v.ToString())" 'OK'
}

function Assert-X64 {
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw '64-bit Windows is required for WDDM HUD self-contained build.'
    }
    Write-Log 'Architecture: x64' 'OK'
}

function Write-RequirementChecklist {
    $notes = Join-Path $InstallRoot 'docs\INSTALLER_NOTES.md'
    Write-Log "Installer notes: $notes" 'INFO'
    Write-Log 'Read-only checks. This installer does not change Defender, the driver blocklist, the power plan, refresh rate, or the default audio device.' 'INFO'

    $build = [Environment]::OSVersion.Version.Build
    if ($build -eq 19045) {
        Write-Log "Windows 10 build $build matches the proven 22H2 ship." 'OK'
    } else {
        Write-Log "Windows 10 build $build. Proven ship is 19045 (22H2). Other builds are unverified." 'WARN'
    }

    try {
        $block = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Config' -Name VulnerableDriverBlocklistEnable -ErrorAction Stop).VulnerableDriverBlocklistEnable
        if ($block -eq 0) {
            Write-Log 'Vulnerable-driver blocklist DWORD is 0.' 'OK'
        } else {
            Write-Log "Vulnerable-driver blocklist DWORD is $block. CPU package temperature needs 0 before the bridge driver will load. See INSTALLER_NOTES.md." 'WARN'
        }
    } catch {
        Write-Log 'Could not read VulnerableDriverBlocklistEnable. Confirm it is 0 before relying on CPU package temperature.' 'WARN'
    }

    try {
        $dg = Get-CimInstance -ClassName Win32_DeviceGuard -Namespace root\Microsoft\Windows\DeviceGuard -ErrorAction Stop
        $hvci = @($dg.SecurityServicesRunning) -contains 2
        if ($hvci) {
            Write-Log 'Memory integrity is running. The temperature driver will not load until it is off.' 'WARN'
        } else {
            Write-Log 'Memory integrity is not running.' 'OK'
        }
    } catch {
        Write-Log 'Could not read memory integrity state.' 'WARN'
    }

    try {
        $mp = Get-MpComputerStatus -ErrorAction Stop
        Write-Log ("Tamper Protection={0}. Real-time protection={1}. Defender reboot required={2}." -f $mp.IsTamperProtected, $mp.RealTimeProtectionEnabled, $mp.RebootRequired) 'INFO'
        if ($mp.RebootRequired) {
            Write-Log 'Defender reports a reboot is required. That reboot is not caused by this installer.' 'WARN'
        }
        $pref = Get-MpPreference -ErrorAction Stop
        $knownPaths = @($pref.ExclusionPath).Count
        $knownProcs = @($pref.ExclusionProcess).Count
        if ($knownPaths -eq 0 -and $knownProcs -eq 0) {
            Write-Log 'Defender exclusions are not visible from this window. An administrator run of installer\Enable-RealtimeWithHudExclusions.ps1 is the setup step.' 'INFO'
        } else {
            $needPaths = @(
                (Join-Path $DistDir 'WddmHud.sys'),
                (Join-Path $DistDir 'WinRing0.sys'),
                (Join-Path $DistDir 'WinRing0x64.sys')
            )
            $needProc = Join-Path $DistDir 'WddmHud.exe'
            $missing = @($needPaths | Where-Object { $pref.ExclusionPath -notcontains $_ })
            if ($pref.ExclusionProcess -notcontains $needProc) { $missing += $needProc }
            if ($mp.RealTimeProtectionEnabled -and $missing.Count -eq 0) {
                Write-Log 'Real-time protection is on. HUD driver exclusions are present.' 'OK'
            } elseif ($missing.Count -gt 0) {
                Write-Log 'HUD Defender exclusions are missing. Run installer\Enable-RealtimeWithHudExclusions.ps1 as administrator.' 'WARN'
            } else {
                Write-Log 'Real-time protection is off. Run installer\Enable-RealtimeWithHudExclusions.ps1 as administrator.' 'WARN'
            }
        }
    } catch {
        Write-Log 'Could not read Defender status.' 'WARN'
    }

    $pawnReady = $false
    try {
        $pawnReady = [bool](Get-Service -Name 'PawnIO' -ErrorAction SilentlyContinue)
    } catch { }
    if (-not $pawnReady) {
        try {
            $pawnReady = [bool](Get-CimInstance Win32_SystemDriver -Filter "Name='PawnIO'" -ErrorAction SilentlyContinue)
        } catch { }
    }
    if ($pawnReady) {
        Write-Log 'PawnIO service is present. Fan RPM can be read when the BIOS answers Dell SMM.' 'OK'
    } else {
        Write-Log 'PawnIO service was not found. Install official PawnIO 2.2.0 with -install -silent before expecting fan RPM. Do not pass -unrestricted.' 'WARN'
    }

    $runName = 'WDDM HUD by pascalkeeper'
    $runValue = $null
    try {
        $runValue = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $runName -ErrorAction SilentlyContinue).$runName
    } catch { }
    if ([string]::IsNullOrWhiteSpace([string]$runValue)) {
        Write-Log 'Sign-in launch is off. The tray item Run on startup turns it on. This installer does not.' 'OK'
    } else {
        Write-Log "Sign-in launch is already on: $runValue" 'INFO'
    }
}

function New-DirectorySafe {
    param([string]$Path)
    if ($DryRun) { Write-Log "Would create directory: $Path" 'DRY'; return }
    if (-not (Test-Path $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
        Write-Log "Created directory: $Path" 'OK'
    }
}

function Save-PreInstallSnapshot {
    $snap = Join-Path $BackupDir ("before-install-{0}" -f $Stamp)
    if ($DryRun) {
        Write-Log "Would create snapshot: $snap" 'DRY'
        return $snap
    }
    New-DirectorySafe -Path $snap
    try {
        powercfg /getactivescheme | Out-File (Join-Path $snap 'active_scheme.txt') -Encoding utf8
        powercfg /list | Out-File (Join-Path $snap 'all_schemes.txt') -Encoding utf8
        reg export 'HKCU\Software\Microsoft\DirectX\UserGpuPreferences' (Join-Path $snap 'UserGpuPreferences.reg') /y 2>$null | Out-Null
        Write-Log "Pre-install snapshot: $snap" 'OK'
    } catch {
        Write-Log "Snapshot partial failure: $($_.Exception.Message)" 'WARN'
    }
    return $snap
}

function Invoke-Build {
    if ($SkipBuild) {
        Write-Log 'SkipBuild specified — using existing dist if present.' 'INFO'
        return
    }
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Log 'dotnet SDK not found. Will use prebuilt dist if available.' 'WARN'
        return
    }
    if (-not (Test-Path $SrcProj)) {
        throw "Project file missing: $SrcProj"
    }

    $publishOut = Join-Path $InstallRoot 'src\WddmHud\bin\Release\net8.0-windows\win-x64\publish'
    $args = @(
        'publish', $SrcProj,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-o', $publishOut
    )

    if ($DryRun) {
        Write-Log ("Would run: dotnet " + ($args -join ' ')) 'DRY'
        return
    }

    Write-Log 'Building self-contained single-file WddmHud.exe (this may take a few minutes)…' 'INFO'
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & dotnet @args 2>&1 | ForEach-Object { Write-Log $_.ToString() 'INFO' }
    $code = $LASTEXITCODE
    $ErrorActionPreference = $oldEap
    if ($code -ne 0) {
        throw "dotnet publish failed with exit code $code"
    }

    $built = Join-Path $publishOut $ExeName
    if (-not (Test-Path $built)) {
        throw "Build succeeded but $ExeName not found at $built"
    }

    New-DirectorySafe -Path $DistDir
    Copy-Item -Path $built -Destination (Join-Path $DistDir $ExeName) -Force
    Write-Log "Published: $(Join-Path $DistDir $ExeName)" 'OK'
}

function Assert-DistPresent {
    $exe = Join-Path $DistDir $ExeName
    if (-not (Test-Path $exe)) {
        throw @"
$ExeName not found at $exe

Options:
  1) Install .NET 8 SDK and re-run without -SkipBuild
  2) Place a prebuilt $ExeName into $DistDir and re-run with -SkipBuild
"@
    }
    $item = Get-Item $exe
    Write-Log ("Dist OK: {0} ({1:N0} bytes, {2})" -f $item.FullName, $item.Length, $item.LastWriteTime) 'OK'
    return $item.FullName
}

function New-Shortcut {
    param(
        [Parameter(Mandatory)][string]$ShortcutPath,
        [Parameter(Mandatory)][string]$TargetPath,
        [string]$WorkingDirectory = '',
        [string]$Description = $ProductName
    )
    if ($DryRun) {
        Write-Log "Would create shortcut: $ShortcutPath -> $TargetPath" 'DRY'
        return
    }
    $dir = Split-Path $ShortcutPath -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    # WScript.Shell COM — creates .lnk that always launches the EXE (no Open-With dialog)
    $w = New-Object -ComObject WScript.Shell
    $sc = $w.CreateShortcut($ShortcutPath)
    $sc.TargetPath = $TargetPath
    if ($WorkingDirectory) { $sc.WorkingDirectory = $WorkingDirectory }
    $sc.Description = $Description
    $sc.WindowStyle = 1
    $sc.Save()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($w) | Out-Null
    Write-Log "Shortcut: $ShortcutPath" 'OK'
}

function Install-Shortcuts {
    param([string]$ExePath)
    if ($NoShortcuts) {
        Write-Log 'NoShortcuts: skipping Start Menu / Desktop links.' 'INFO'
        return
    }
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\WDDM HUD by pascalkeeper'
    New-DirectorySafe -Path $startMenu
    New-Shortcut -ShortcutPath (Join-Path $startMenu 'WDDM HUD.lnk') -TargetPath $ExePath -WorkingDirectory $DistDir
    New-Shortcut -ShortcutPath (Join-Path $startMenu 'Uninstall WDDM HUD.lnk') `
        -TargetPath (Join-Path $InstallRoot 'installer\Uninstall-WddmHud.bat') `
        -WorkingDirectory (Join-Path $InstallRoot 'installer') `
        -Description 'Uninstall WDDM HUD by pascalkeeper'

    $desktop = [Environment]::GetFolderPath('Desktop')
    New-Shortcut -ShortcutPath (Join-Path $desktop 'WDDM HUD.lnk') -TargetPath $ExePath -WorkingDirectory $DistDir
}

function Register-UninstallKey {
    param([string]$ExePath)
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WddmHud_by_pascalkeeper'
    if ($DryRun) {
        Write-Log "Would write uninstall registry key: $key" 'DRY'
        return
    }
    New-Item -Path $key -Force | Out-Null
    Set-ItemProperty -Path $key -Name 'DisplayName' -Value $ProductName
    Set-ItemProperty -Path $key -Name 'Publisher' -Value 'pascalkeeper'
    Set-ItemProperty -Path $key -Name 'DisplayVersion' -Value '1.0.0'
    Set-ItemProperty -Path $key -Name 'InstallLocation' -Value $InstallRoot
    Set-ItemProperty -Path $key -Name 'DisplayIcon' -Value $ExePath
    Set-ItemProperty -Path $key -Name 'UninstallString' -Value ("`"{0}`"" -f (Join-Path $InstallRoot 'installer\Uninstall-WddmHud.bat'))
    Set-ItemProperty -Path $key -Name 'NoModify' -Value 1 -Type DWord
    Set-ItemProperty -Path $key -Name 'NoRepair' -Value 1 -Type DWord
    Write-Log 'Registered HKCU uninstall entry (Add/Remove Programs).' 'OK'
}

function Invoke-OptionalScripts {
    $scripts = Join-Path $InstallRoot 'scripts'
    if ($ApplyGameGpuPrefs) {
        $p = Join-Path $scripts 'Apply-GameGpuPreferences.ps1'
        if (Test-Path $p) {
            Write-Log 'Applying game GPU preferences…' 'INFO'
            if ($DryRun) {
                & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $p -DryRun
            } else {
                & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $p
            }
        } else {
            Write-Log "Missing $p" 'WARN'
        }
    }
    if ($CleanDuplicatePowerPlans) {
        $p = Join-Path $scripts 'Clean-DuplicatePowerPlans.ps1'
        if (Test-Path $p) {
            Write-Log 'Cleaning duplicate power plan remnants…' 'INFO'
            if ($DryRun) {
                & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $p -DryRun
            } else {
                # Power plan delete needs elevation
                if (-not (Test-IsAdministrator)) {
                    Write-Log 'Elevation required for power plan cleanup — re-launching elevated…' 'WARN'
                    $arg = "-NoProfile -ExecutionPolicy Bypass -File `"$p`""
                    Start-Process powershell.exe -Verb RunAs -ArgumentList $arg -Wait
                } else {
                    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $p
                }
            }
        } else {
            Write-Log "Missing $p" 'WARN'
        }
    }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
try {
    Write-Host ''
    Write-Host '============================================================' -ForegroundColor Green
    Write-Host '  WDDM HUD by pascalkeeper — Installer v1.0.0' -ForegroundColor Green
    Write-Host '  Offline · Windows 10 x64 · No telemetry' -ForegroundColor Green
    if ($DryRun) { Write-Host '  *** DRY-RUN MODE — no permanent changes ***' -ForegroundColor Yellow }
    Write-Host '============================================================' -ForegroundColor Green
    Write-Host ''

    Assert-Windows10Plus
    Assert-X64
    Write-RequirementChecklist

    if (-not (Test-Path 'F:\')) {
        Write-Log 'F:\ not available. Install root prefers F:\; installer will still try the path.' 'WARN'
    }

    New-DirectorySafe -Path $InstallRoot
    New-DirectorySafe -Path $LogDir
    New-DirectorySafe -Path $DistDir
    New-DirectorySafe -Path $BackupDir
    New-DirectorySafe -Path (Join-Path $InstallRoot 'config')

    # Kill ghost / old-version instances BEFORE overwriting dist (file locks + no stacks)
    $ghostScript = Join-Path $InstallRoot 'scripts\Stop-WddmHudGhosts.ps1'
    if (Test-Path $ghostScript) {
        Write-Log 'Stopping any running WDDM HUD instances (ghost/old version prevention)…' 'INFO'
        if ($DryRun) {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ghostScript -DryRun -AlsoRemoveBuildRemnants
        } else {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ghostScript -AlsoRemoveBuildRemnants
        }
    } else {
        Get-Process -Name 'WddmHud' -ErrorAction SilentlyContinue | ForEach-Object {
            if ($DryRun) { Write-Log "Would stop PID $($_.Id)" 'DRY' }
            else { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue; Write-Log "Stopped PID $($_.Id)" 'OK' }
        }
    }

    $null = Save-PreInstallSnapshot
    Invoke-Build
    $exePath = Assert-DistPresent
    Install-Shortcuts -ExePath $exePath
    Register-UninstallKey -ExePath $exePath
    Invoke-OptionalScripts

    # Default: also apply game GPU prefs + clean plans when user didn't pass flags
    # (installer .bat passes them explicitly)

    Write-Log 'Install complete.' 'OK'
    Write-Log "Executable: $exePath" 'OK'
    Write-Log "Log: $LogFile" 'INFO'
    Write-Host ''
    Write-Host 'Launch via Desktop shortcut or Start Menu — always the .exe.' -ForegroundColor Green
    Write-Host 'Never associate .ps1/.cs as the daily run path.' -ForegroundColor DarkGray
    Write-Host ''

    if ($StartAfterInstall -and -not $DryRun) {
        Write-Log 'Starting WDDM HUD…' 'INFO'
        Start-Process -FilePath $exePath -WorkingDirectory $DistDir
    }

    exit 0
}
catch {
    Write-Log ("INSTALL FAILED: " + $_.Exception.Message) 'ERROR'
    Write-Log ($_.ScriptStackTrace) 'ERROR'
    Write-Host ''
    Write-Host 'Installer failed. See log:' -ForegroundColor Red
    Write-Host $LogFile -ForegroundColor Yellow
    Write-Host 'Emergency restore: installer\EmergencyRestore.bat' -ForegroundColor Yellow
    exit 1
}
