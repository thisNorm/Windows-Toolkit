@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /codepage:65001 /target:exe /out:"%~dp0bin\NetworkReaderTests.exe" /reference:System.dll /reference:System.Core.dll "%~dp0src\NetworkReader.cs" "%~dp0tests\NetworkReaderTests.cs"
if errorlevel 1 exit /b 1
"%~dp0bin\NetworkReaderTests.exe"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /codepage:65001 /target:exe /out:"%~dp0bin\ThemeSettingsTests.exe" /reference:System.dll /reference:System.Core.dll "%~dp0src\ThemeSettings.cs" "%~dp0tests\ThemeSettingsTests.cs"
if errorlevel 1 exit /b 1
"%~dp0bin\ThemeSettingsTests.exe"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /codepage:65001 /target:exe /main:ThemeStyleTests /out:"%~dp0bin\ThemeStyleTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "%~dp0src\*.cs" "%~dp0tests\ThemeStyleTests.cs"
if errorlevel 1 exit /b 1
"%~dp0bin\ThemeStyleTests.exe"
exit /b %errorlevel%
