@echo off
setlocal EnableExtensions
title WDDM HUD — Emergency Restore
REM Restores GPU preference registry from newest backup if present.
REM Does NOT change the active power plan (per project coordinator decision).
cd /d "%~dp0"
set "ROOT=F:\imonlinegaming\WDDM_HUD_by_pascalkeeper"
set "BACKUP=%ROOT%\backups"

echo ============================================================
echo  WDDM HUD Emergency Restore
echo  - Stops WddmHud.exe
echo  - Restores newest UserGpuPreferences.reg backup if found
echo  - Does NOT switch power plans
echo ============================================================
echo.

taskkill /IM WddmHud.exe /F >nul 2>&1

if not exist "%BACKUP%" (
  echo [WARN] No backups folder at %BACKUP%
  goto :mother
)

set "NEWEST="
for /f "delims=" %%D in ('dir /b /ad /o-d "%BACKUP%\before-install-*" 2^>nul') do (
  if not defined NEWEST set "NEWEST=%%D"
)
if not defined NEWEST (
  echo [WARN] No before-install-* snapshot found.
  goto :mother
)

set "REGFILE=%BACKUP%\%NEWEST%\UserGpuPreferences.reg"
if exist "%REGFILE%" (
  echo Restoring %REGFILE%
  reg import "%REGFILE%"
  if errorlevel 1 (
    echo [ERROR] reg import failed
  ) else (
    echo [OK] UserGpuPreferences restored from %NEWEST%
  )
) else (
  echo [WARN] No UserGpuPreferences.reg in %NEWEST%
)

:mother
if exist "F:\motherearth\EmergencyRestore.bat" (
  echo.
  echo Mother Earth emergency restore is available at:
  echo   F:\motherearth\EmergencyRestore.bat
  echo Run it separately if you need ASPM/power emergency undo.
)
if exist "F:\motherearth\dist\MotherEarth.exe" (
  echo Or: F:\motherearth\dist\MotherEarth.exe --emergency-restore
)

echo.
echo Done.
pause
exit /b 0
