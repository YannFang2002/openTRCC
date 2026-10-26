@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-startup-task.ps1"
exit /b %ERRORLEVEL%
