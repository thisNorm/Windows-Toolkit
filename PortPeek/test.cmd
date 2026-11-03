@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /codepage:65001 /target:exe /out:"%~dp0bin\PortReaderTests.exe" /reference:System.dll /reference:System.Core.dll "%~dp0src\PortReader.cs" "%~dp0tests\PortReaderTests.cs"
if errorlevel 1 exit /b 1
"%~dp0bin\PortReaderTests.exe"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /codepage:65001 /target:exe /out:"%~dp0bin\PortViewTests.exe" /reference:System.dll /reference:System.Core.dll "%~dp0src\PortReader.cs" "%~dp0src\FavoriteStore.cs" "%~dp0src\PortView.cs" "%~dp0tests\PortViewTests.cs"
if errorlevel 1 exit /b 1
"%~dp0bin\PortViewTests.exe"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /codepage:65001 /target:exe /out:"%~dp0bin\FavoriteStoreTests.exe" /reference:System.dll /reference:System.Core.dll "%~dp0src\PortReader.cs" "%~dp0src\FavoriteStore.cs" "%~dp0tests\FavoriteStoreTests.cs"
if errorlevel 1 exit /b 1
"%~dp0bin\FavoriteStoreTests.exe"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /codepage:65001 /target:exe /out:"%~dp0bin\ThemeSettingsTests.exe" /reference:System.dll /reference:System.Core.dll "%~dp0src\ThemeSettings.cs" "%~dp0tests\ThemeSettingsTests.cs"
if errorlevel 1 exit /b 1
"%~dp0bin\ThemeSettingsTests.exe"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /codepage:65001 /target:exe /main:ThemeStyleTests /out:"%~dp0bin\ThemeStyleTests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "%~dp0src\*.cs" "%~dp0tests\ThemeStyleTests.cs"
if errorlevel 1 exit /b 1
"%~dp0bin\ThemeStyleTests.exe"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /codepage:65001 /target:exe /out:"%~dp0bin\PortActionsTests.exe" /reference:System.dll /reference:System.Core.dll "%~dp0src\PortReader.cs" "%~dp0src\PortActions.cs" "%~dp0tests\PortActionsTests.cs"
if errorlevel 1 exit /b 1
"%~dp0bin\PortActionsTests.exe"
exit /b %errorlevel%
