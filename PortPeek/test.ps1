$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$destination = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
& $compiler /nologo /codepage:65001 /target:exe "/out:$destination\PortReaderTests.exe" /reference:System.dll /reference:System.Core.dll `
    (Join-Path $PSScriptRoot 'src\PortReader.cs') (Join-Path $PSScriptRoot 'tests\PortReaderTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Reader test build failed.' }
& (Join-Path $destination 'PortReaderTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Reader tests failed.' }
& $compiler /nologo /codepage:65001 /target:exe "/out:$destination\PortViewTests.exe" /reference:System.dll /reference:System.Core.dll `
    (Join-Path $PSScriptRoot 'src\PortReader.cs') (Join-Path $PSScriptRoot 'src\FavoriteStore.cs') (Join-Path $PSScriptRoot 'src\PortView.cs') (Join-Path $PSScriptRoot 'tests\PortViewTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'View test build failed.' }
& (Join-Path $destination 'PortViewTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'View tests failed.' }
& $compiler /nologo /codepage:65001 /target:exe "/out:$destination\FavoriteStoreTests.exe" /reference:System.dll /reference:System.Core.dll `
    (Join-Path $PSScriptRoot 'src\PortReader.cs') (Join-Path $PSScriptRoot 'src\FavoriteStore.cs') (Join-Path $PSScriptRoot 'tests\FavoriteStoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Favorite test build failed.' }
& (Join-Path $destination 'FavoriteStoreTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Favorite tests failed.' }
& $compiler /nologo /codepage:65001 /target:exe "/out:$destination\ThemeSettingsTests.exe" /reference:System.dll /reference:System.Core.dll `
    (Join-Path $PSScriptRoot 'src\ThemeSettings.cs') (Join-Path $PSScriptRoot 'tests\ThemeSettingsTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Theme setting test build failed.' }
& (Join-Path $destination 'ThemeSettingsTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Theme setting tests failed.' }
$appSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
& $compiler /nologo /codepage:65001 /target:exe /main:ThemeStyleTests "/out:$destination\ThemeStyleTests.exe" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll @appSources (Join-Path $PSScriptRoot 'tests\ThemeStyleTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Theme style test build failed.' }
& (Join-Path $destination 'ThemeStyleTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Theme style tests failed.' }
& $compiler /nologo /codepage:65001 /target:exe "/out:$destination\PortActionsTests.exe" /reference:System.dll /reference:System.Core.dll `
    (Join-Path $PSScriptRoot 'src\PortReader.cs') (Join-Path $PSScriptRoot 'src\PortActions.cs') (Join-Path $PSScriptRoot 'tests\PortActionsTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Action test build failed.' }
& (Join-Path $destination 'PortActionsTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Action tests failed.' }
