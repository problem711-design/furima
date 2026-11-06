@echo off
chcp 65001 > nul
echo === Furima PC Sync 起動ツール ===
powershell -ExecutionPolicy Bypass -File "%~dp0furima-sync.ps1"
pause
