@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Iniciar.ps1" -Demo
if errorlevel 1 pause
