param([string]$DotnetPath = 'dotnet', [string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src/YingView/YingView.csproj'
$destination = Join-Path $projectRoot 'dist/YingView'
& $DotnetPath run --project (Join-Path $projectRoot 'tests/YingView.Core.Tests') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Geometry tests failed.' }
& $DotnetPath publish $project -c $Configuration -r win-x64 --self-contained true -o $destination --nologo `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
foreach ($file in @('LICENSE','NOTICE','THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination (Join-Path $destination $file) -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination (Join-Path $destination 'Install.ps1') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'ThirdParty') -Destination $destination -Recurse -Force
Write-Output "Client: $destination"
