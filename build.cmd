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

call :compile winexe PinyinPractice.exe PinyinPractice.cs
if errorlevel 1 exit /b 1
call :compile exe PinyinPracticeTests.exe PinyinPractice.cs
if errorlevel 1 exit /b 1
call :compile_math winexe ArithmeticPractice.exe
if errorlevel 1 exit /b 1
call :compile_math exe ArithmeticPracticeTests.exe
if errorlevel 1 exit /b 1
call :compile winexe PracticeToolkit.exe PracticeToolkit.cs
if errorlevel 1 exit /b 1

echo [OK] PracticeToolkit.exe, PinyinPractice.exe and ArithmeticPractice.exe created.
exit /b 0

:compile_math
set "TARGET=%~1"
set "OUTPUT=%~2"
echo Building %OUTPUT%...
"%CSC%" /nologo /target:%TARGET% /out:"%~dp0%OUTPUT%" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:"%SPEECH%" "%~dp0ArithmeticEngine.cs" "%~dp0ArithmeticPractice.cs"
if errorlevel 1 (
  echo [ERROR] Failed to build %OUTPUT%.
  exit /b 1
)
exit /b 0

:compile
set "TARGET=%~1"
set "OUTPUT=%~2"
set "SOURCE=%~3"
echo Building %OUTPUT%...
"%CSC%" /nologo /target:%TARGET% /out:"%~dp0%OUTPUT%" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:"%SPEECH%" "%~dp0%SOURCE%"
if errorlevel 1 (
  echo [ERROR] Failed to build %OUTPUT%.
  exit /b 1
)
exit /b 0
