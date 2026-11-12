@echo off
setlocal enabledelayedexpansion
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] csc.exe not found. Please install .NET Framework 4.x.
  exit /b 1
)

set "SPEECH="
for /f "delims=" %%F in ('dir /b /s "%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35\System.Speech.dll" 2^>nul') do set "SPEECH=%%F"
if not defined SPEECH (
  for /f "delims=" %%F in ('dir /b /s "%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\System.Speech\3.0.0.0__31bf3856ad364e35\System.Speech.dll" 2^>nul') do set "SPEECH=%%F"
)
if not defined SPEECH (
  echo [ERROR] System.Speech.dll not found in GAC. Speech output unavailable.
  exit /b 1
)

echo Using compiler: %CSC%
echo Using System.Speech: %SPEECH%
"%CSC%" /nologo /target:winexe /out:"%~dp0PinyinPractice.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:"%SPEECH%" "%~dp0PinyinPractice.cs"

if errorlevel 1 (
  echo [ERROR] Build failed.
  exit /b 1
)

"%CSC%" /nologo /target:exe /out:"%~dp0PinyinPracticeTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:"%SPEECH%" "%~dp0PinyinPractice.cs"
if errorlevel 1 (
  echo [WARN] Test executable build failed.
)

echo [OK] PinyinPractice.exe created.
exit /b 0
