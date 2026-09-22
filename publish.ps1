param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64', [string]$OutputDirectory = 'artifacts\official', [switch]$UnoDesktop)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\OhMyHarness.App\OhMyHarness.App.csproj'
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }
$platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }
$framework = if ($UnoDesktop) { 'net10.0-desktop' } else { 'net10.0-windows10.0.19041.0' }
dotnet publish $project -f $framework -p:OhMyHarnessDesktopOnly=$($UnoDesktop.IsPresent.ToString().ToLowerInvariant()) -c Release -r $Runtime --self-contained true -p:PlatformTarget=$platform -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'Publication échouée.' }
$skillsTarget = Join-Path $output 'skills\exemple-revue'
if (!(Test-Path -LiteralPath $skillsTarget)) {
    New-Item -ItemType Directory -Path (Join-Path $output 'skills') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'skills\exemple-revue') -Destination $skillsTarget -Recurse
}
Write-Host "Publication disponible : $output\OhMyHarness.App.exe"
