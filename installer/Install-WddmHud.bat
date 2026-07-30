@echo off
setlocal EnableExtensions EnableDelayedExpansion
REM =============================================================================
REM  WDDM HUD by pascalkeeper — One-click installer launcher (Windows 10)
REM  ALWAYS use this .bat for install — it binds to powershell.exe explicitly.
REM  Prevents "How do you want to open this file?" association dialogs.
REM =============================================================================

title WDDM HUD by pascalkeeper — Installer
cd /d "%~dp0"
if errorlevel 1 (
  echo [ERROR] Cannot cd to installer directory.
  pause
  exit /b 1
)

set "ROOT=F:\imonlinegaming\WDDM_HUD_by_pascalkeeper"
set "PS1=%~dp0Install-WddmHud.ps1"
set "LOGDIR=%ROOT%\logs"

if not exist "%PS1%" (
  echo [ERROR] Missing Install-WddmHud.ps1 next to this bat.
  pause
  exit /b 1
)

where powershell.exe >nul 2>&1
if errorlevel 1 (
  echo [ERROR] powershell.exe not found. Windows 10 is required.
  pause
  exit /b 1
)

echo.
echo ============================================================
echo   WDDM HUD by pascalkeeper — Installer
echo   Root: %ROOT%
echo ============================================================
echo.
echo   [1] Full install (build + shortcuts + game GPU prefs + clean dupe power plans)
echo   [2] Dry-run only (no changes)
echo   [3] Install without rebuild (use existing dist\WddmHud.exe)
echo   [4] Exit
echo.
set /p CHOICE=Select option [1-4]: 

if "%CHOICE%"=="4" exit /b 0
if "%CHOICE%"=="2" (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PS1%" -DryRun -ApplyGameGpuPrefs -CleanDuplicatePowerPlans
  set ERR=!ERRORLEVEL!
  goto :done
)
if "%CHOICE%"=="3" (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PS1%" -SkipBuild -ApplyGameGpuPrefs -CleanDuplicatePowerPlans -StartAfterInstall
  set ERR=!ERRORLEVEL!
  goto :done
)
REM default / 1
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PS1%" -ApplyGameGpuPrefs -CleanDuplicatePowerPlans -StartAfterInstall
set ERR=!ERRORLEVEL!

:done
echo.
if not "%ERR%"=="0" (
  echo [FAILED] Exit code %ERR%
  echo See logs under %LOGDIR%
  echo Emergency: EmergencyRestore.bat
  pause
  exit /b %ERR%
)
echo [OK] Installer finished.
pause
exit /b 0
