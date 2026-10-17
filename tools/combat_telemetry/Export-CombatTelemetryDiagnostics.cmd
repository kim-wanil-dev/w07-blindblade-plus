@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Export-CombatTelemetryDiagnostics.ps1"
pause
