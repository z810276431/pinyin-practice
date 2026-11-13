@echo off
setlocal
call "%~dp0build.cmd"
if errorlevel 1 exit /b 1

"%~dp0PinyinPracticeTests.exe" --selftest
if errorlevel 1 exit /b 1
"%~dp0ArithmeticPracticeTests.exe" --selftest
if errorlevel 1 exit /b 1

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

"%CSC%" /nologo /target:exe /out:"%~dp0KeyboardTests.exe" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:"%~dp0PinyinPractice.exe" "%~dp0tests\KeyboardTests.cs"
if errorlevel 1 exit /b 1
"%~dp0KeyboardTests.exe"
if errorlevel 1 exit /b 1

"%CSC%" /nologo /target:exe /out:"%~dp0ArithmeticKeyboardTests.exe" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:"%~dp0ArithmeticPractice.exe" "%~dp0tests\ArithmeticKeyboardTests.cs"
if errorlevel 1 exit /b 1
"%~dp0ArithmeticKeyboardTests.exe"
if errorlevel 1 exit /b 1

"%CSC%" /nologo /target:exe /out:"%~dp0ToolkitTests.exe" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:"%~dp0PracticeToolkit.exe" "%~dp0tests\ToolkitTests.cs"
if errorlevel 1 exit /b 1
"%~dp0ToolkitTests.exe"
exit /b %errorlevel%
