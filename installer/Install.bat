@echo off
rem Double-click launcher for the Ember Knights Archipelago installer.
rem It runs the PowerShell script next to it. -ExecutionPolicy Bypass applies to this one run only and changes no setting.
title Ember Knights Archipelago - installer
cd /d "%~dp0"
if not exist "%~dp0Install-EmberKnightsArchipelago.ps1" (
    echo Install-EmberKnightsArchipelago.ps1 was not found next to this file.
    echo Extract the whole zip first, then run Install.bat from the extracted folder.
    pause
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-EmberKnightsArchipelago.ps1" %*
echo.
pause
