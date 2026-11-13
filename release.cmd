@echo off
setlocal
rem Prefer Windows PowerShell 5.1: it is present on every supported Windows version.
rem Fall back to pwsh only when powershell.exe is unavailable.
where powershell >nul 2>nul
if %errorlevel%==0 (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" %*
) else (
  pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" %*
)
exit /b %errorlevel%
