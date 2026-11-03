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
