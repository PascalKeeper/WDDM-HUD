@echo off
setlocal EnableExtensions
title WDDM HUD by pascalkeeper — Uninstall
cd /d "%~dp0"
echo Uninstalling WDDM HUD by pascalkeeper...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-WddmHud.ps1"
echo.
pause
exit /b %ERRORLEVEL%
