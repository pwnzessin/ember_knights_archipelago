@echo off
rem Removes the Ember Knights Archipelago mod (plugin + its config). BepInEx stays installed.
rem To remove BepInEx as well, run:  Install.bat -Uninstall -RemoveBepInEx
title Ember Knights Archipelago - uninstall
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-EmberKnightsArchipelago.ps1" -Uninstall %*
echo.
pause
