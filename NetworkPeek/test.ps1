$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$destination = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
& $compiler /nologo /codepage:65001 /target:exe "/out:$destination\NetworkReaderTests.exe" `
    /reference:System.dll /reference:System.Core.dll `
    (Join-Path $PSScriptRoot 'src\NetworkReader.cs') (Join-Path $PSScriptRoot 'tests\NetworkReaderTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& (Join-Path $destination 'NetworkReaderTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'NetworkPeek tests failed.' }
& $compiler /nologo /codepage:65001 /target:exe "/out:$destination\ThemeSettingsTests.exe" /reference:System.dll /reference:System.Core.dll `
    (Join-Path $PSScriptRoot 'src\ThemeSettings.cs') (Join-Path $PSScriptRoot 'tests\ThemeSettingsTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Theme setting test build failed.' }
& (Join-Path $destination 'ThemeSettingsTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Theme setting tests failed.' }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
& $compiler /nologo /codepage:65001 /target:exe /main:ThemeStyleTests "/out:$destination\ThemeStyleTests.exe" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll @sources (Join-Path $PSScriptRoot 'tests\ThemeStyleTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Theme UI test build failed.' }
& (Join-Path $destination 'ThemeStyleTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Theme UI tests failed.' }
