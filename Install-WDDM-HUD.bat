@echo off
setlocal EnableExtensions
REM =============================================================================
REM  WDDM HUD by pascalkeeper — one click installer
REM  Copyright (C) 2026 Joseph Rocco Peransi
REM  www.github.com/pascalkeeper
REM  Double-click this file. It installs the exe and shortcuts, then starts the HUD.
REM =============================================================================

title WDDM HUD Setup
cd /d "%~dp0"
if errorlevel 1 (
  echo [ERROR] Cannot open the installer folder.
  pause
  exit /b 1
)

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"
set "PS1=%ROOT%\installer\Install-WddmHud.ps1"

if not exist "%PS1%" (
  echo [ERROR] Missing installer\Install-WddmHud.ps1
  pause
  exit /b 1
)

where powershell.exe >nul 2>&1
if errorlevel 1 (
  echo [ERROR] powershell.exe was not found. Windows 10 64-bit is required.
  pause
  exit /b 1
)

echo.
echo WDDM HUD setup
echo Copyright (C) 2026 Joseph Rocco Peransi
echo www.github.com/pascalkeeper
echo.

set "EXTRA="
if exist "%ROOT%\dist\WddmHud.exe" set "EXTRA=-SkipBuild"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PS1%" -InstallRoot "%ROOT%" %EXTRA% -StartAfterInstall
set "ERR=%ERRORLEVEL%"
echo.
if not "%ERR%"=="0" (
  echo [FAILED] Exit code %ERR%
  echo See the log in %ROOT%\logs
  pause
  exit /b %ERR%
)
echo [OK] WDDM HUD is installed.
pause
exit /b 0
