@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-desktop.ps1" -Zip -CreateShortcut
if errorlevel 1 (
    echo Build failed. Review the error above.
    pause
    exit /b 1
)
pause
