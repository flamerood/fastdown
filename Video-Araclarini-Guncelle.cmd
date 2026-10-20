@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Install-VideoTools.ps1"
if errorlevel 1 (echo Video araclari guncellenemedi.) else (echo Video araclari hazir.)
pause
