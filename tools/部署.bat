@echo off
chcp 65001 >nul
where pwsh.exe >nul 2>nul
if not errorlevel 1 (
    pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0部署.ps1" %*
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0部署.ps1" %*
)
pause
