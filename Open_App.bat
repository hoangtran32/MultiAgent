@echo off
if not exist "%~dp0app-bin\MultiAgentHub.exe" (
    echo App has not been built. Run build-desktop.bat first.
    pause
    exit /b 1
)
start "" "%~dp0app-bin\MultiAgentHub.exe"
