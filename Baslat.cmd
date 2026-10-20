@echo off
cd /d "%~dp0"
if exist "Fastdown\Fastdown.exe" (
    start "" "Fastdown\Fastdown.exe"
) else (
    echo Uygulama henuz derlenmemis. README.md dosyasindaki adimlari izleyin.
    pause
)
