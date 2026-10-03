@echo off
rem Installs the Turn Telemetry plugin and dashboard into SimHub.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
pause
