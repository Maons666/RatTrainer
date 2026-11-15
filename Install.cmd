@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Manage-RatTrainer.ps1" -Action Install %*
pause
