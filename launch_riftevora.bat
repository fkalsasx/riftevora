@echo off
setlocal
set "APP_EXE=%~dp0dist\Riftevora-3.0.0-win-x64\Riftevora.exe"
if not exist "%APP_EXE%" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build_exe.ps1" -NoOpen
if not exist "%APP_EXE%" exit /b 1
start "" "%APP_EXE%"
