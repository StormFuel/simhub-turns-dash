@echo off
rem Removes the Turn Telemetry plugin and dashboard from SimHub (keeps your turn edits).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -Uninstall %*
pause
