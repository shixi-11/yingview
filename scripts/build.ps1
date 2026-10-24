param([string]$DotnetPath = 'dotnet', [string]$Configuration = 'Release', [string]$OutputDirectory = '', [ValidateSet('auto','zh-CN','en-US','all')][string]$ClientLanguage = 'all')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src/YingView/YingView.csproj'
$projectXml = [xml](Get-Content -LiteralPath $project -Raw)
$projectVersion = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
$outputRoot = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } elseif ($ClientLanguage -eq 'all') { Join-Path $projectRoot 'dist' } else { Join-Path $projectRoot "dist/YingView-$ClientLanguage" }
& $DotnetPath run --project (Join-Path $projectRoot 'tests/YingView.Core.Tests') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Geometry tests failed.' }
$languages = if ($ClientLanguage -eq 'all') { @('zh-CN','en-US') } else { @($ClientLanguage) }
foreach ($language in $languages) {
    $displayName = if ($language -eq 'zh-CN') { '映窗' } else { 'YingView' }
    $destination = if ($ClientLanguage -eq 'all') { Join-Path $outputRoot "YingView-$language" } else { $outputRoot }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    & $DotnetPath publish $project -c $Configuration -r win-x64 --self-contained true -o $destination --nologo "-p:ClientLanguage=$language" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $language." }
    foreach ($file in @('LICENSE','NOTICE','CLIENT-LICENSE.txt','CLIENT-LICENSE.zh-CN.txt','LICENSING.md','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination (Join-Path $destination $file) -Force }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination (Join-Path $destination 'Install.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ShortcutIdentity.cs') -Destination (Join-Path $destination 'ShortcutIdentity.cs') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'ThirdParty') -Destination $destination -Recurse -Force
    [ordered]@{ Language=$language; DisplayName=$displayName; Version=$projectVersion } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'client-language.json') -Encoding utf8
    Write-Output "Client: $destination ($language)"
}
