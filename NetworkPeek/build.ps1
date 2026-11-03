$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$destination = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
& $compiler /nologo /codepage:65001 /target:winexe /optimize+ /warn:4 /platform:anycpu `
    "/out:$destination\NetworkPeek.exe" "/win32manifest:$PSScriptRoot\app.manifest" `
    "/win32icon:$PSScriptRoot\assets\networkpeek.ico" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll @sources
if ($LASTEXITCODE -ne 0) { throw 'NetworkPeek build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NetworkPeek.exe.config') -Destination $destination -Force
Write-Host "Built: $destination\NetworkPeek.exe"
