@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /codepage:65001 /target:exe /out:"%~dp0bin\NetworkReaderTests.exe" /reference:System.dll /reference:System.Core.dll "%~dp0src\NetworkReader.cs" "%~dp0tests\NetworkReaderTests.cs"
if errorlevel 1 exit /b 1
"%~dp0bin\NetworkReaderTests.exe"
exit /b %errorlevel%
