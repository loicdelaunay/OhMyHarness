$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$run = Join-Path $repo ('artifacts\portable-check-' + [Guid]::NewGuid().ToString('N'))
$first = Join-Path $run 'first'
$second = Join-Path $run 'moved'
dotnet publish (Join-Path $PSScriptRoot 'PortableStorage.Probe') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o $first
if ($LASTEXITCODE -ne 0) { throw 'Probe publication failed' }
Push-Location $repo
try {
    & (Join-Path $first 'PortableStorage.Probe.exe') $first
    if ($LASTEXITCODE -ne 0) { throw 'Initial portable path test failed' }
    New-Item -ItemType Directory -Path $second | Out-Null
    Copy-Item -LiteralPath (Join-Path $first 'PortableStorage.Probe.exe') -Destination $second
    & (Join-Path $second 'PortableStorage.Probe.exe') $second
    if ($LASTEXITCODE -ne 0) { throw 'Moved portable path test failed' }
} finally { Pop-Location }
