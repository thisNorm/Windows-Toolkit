@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /warn:4 /platform:anycpu /out:"%~dp0bin\NetworkPeek.exe" /win32manifest:"%~dp0app.manifest" /win32icon:"%~dp0assets\networkpeek.ico" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "%~dp0src\*.cs"
if errorlevel 1 exit /b 1
copy /y "%~dp0NetworkPeek.exe.config" "%~dp0bin\NetworkPeek.exe.config" >nul
echo Built: %~dp0bin\NetworkPeek.exe
