[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$IdentityName,
    [Parameter(Mandatory)][string]$Publisher,
    [Parameter(Mandatory)][string]$PublisherDisplayName,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$ClientDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Require-Text([string]$Value, [string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match '^\s*\[.*\]\s*$' -or $Value -match '^(TODO|CHANGE.?ME|PLACEHOLDER)$') {
        throw "$Name must be a real value from the Microsoft Store Partner Center; placeholder and sample identities are not accepted."
    }
}

Require-Text $IdentityName 'IdentityName'
Require-Text $Publisher 'Publisher'
Require-Text $PublisherDisplayName 'PublisherDisplayName'

if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$') {
    throw 'IdentityName must match the reserved Store identity format: 3–50 ASCII letters, digits, periods, or hyphens.'
}
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw 'Version must have four numeric parts and end in .0, for example 0.2.0.0.'
}
$parsedVersion = [version]$Version
if ($parsedVersion.Major -gt 65535 -or $parsedVersion.Minor -gt 65535 -or $parsedVersion.Build -gt 65535) {
    throw 'The first three Version components must each be between 0 and 65535.'
}

$clientPath = (Resolve-Path -LiteralPath $ClientDirectory -ErrorAction Stop).Path
$clientPrefix = $clientPath.TrimEnd('\') + '\'
$exePath = Join-Path $clientPath 'YingView.exe'
$languageMetadataPath = Join-Path $clientPath 'client-language.json'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) { throw "ClientDirectory must contain the compiled standalone YingView.exe: $exePath" }
if (-not (Test-Path -LiteralPath $languageMetadataPath -PathType Leaf)) { throw "ClientDirectory must contain client-language.json identifying a generic-language build: $languageMetadataPath" }
try { $languageMetadata = Get-Content -LiteralPath $languageMetadataPath -Raw | ConvertFrom-Json }
catch { throw "Could not read client-language.json: $($_.Exception.Message)" }
if ([string]$languageMetadata.Language -cne 'auto') {
    throw 'ClientDirectory must be a generic build with client-language.json Language set to auto.'
}

$sourceAssets = Join-Path $PSScriptRoot '..\src\YingView\Assets'
$appIconPath = Join-Path $sourceAssets 'app-icon.png'
if (-not (Test-Path -LiteralPath $appIconPath -PathType Leaf)) {
    throw "The universal app icon was not found: $appIconPath"
}
$sourceIcon = [Drawing.Bitmap]::new((Resolve-Path -LiteralPath $appIconPath).Path)
try {
    if ($sourceIcon.Width -ne 1024 -or $sourceIcon.Height -ne 1024) {
        throw "The universal app icon must be 1024x1024 pixels; found $($sourceIcon.Width)x$($sourceIcon.Height)."
    }
}
finally { $sourceIcon.Dispose() }

$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$sdkTools = Get-ChildItem -LiteralPath $sdkRoot -Recurse -Filter MakeAppx.exe -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\MakeAppx\.exe$' } |
    Sort-Object { if ($_.Directory.Parent.Name -match '^\d+\.\d+\.\d+\.\d+$') { [version]$_.Directory.Parent.Name } else { [version]'0.0' } } -Descending |
    Select-Object -First 1
if (-not $sdkTools) {
    throw "MakeAppx.exe was not found under '$sdkRoot'. Install the Windows 10/11 SDK (Appx Packaging tools), then retry."
}
$makeAppx = $sdkTools.FullName
$makePri = Join-Path $sdkTools.Directory.FullName 'MakePri.exe'
if (-not (Test-Path -LiteralPath $makePri -PathType Leaf)) {
    throw "MakePri.exe was not found beside '$makeAppx'. Install the Windows SDK MakePri tools, then retry."
}

$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if ($outputPath.TrimEnd('\') -eq $clientPath -or $outputPath.StartsWith($clientPrefix, [StringComparison]::OrdinalIgnoreCase) -or $clientPath.StartsWith($outputPath.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be separate from ClientDirectory and its parent/child folders.'
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$packagePath = Join-Path $outputPath ("{0}_{1}_x64.msix" -f $IdentityName, $Version)
$stagePath = Join-Path $outputPath ('.store-stage-' + [guid]::NewGuid().ToString('N'))
$verifyPath = Join-Path $outputPath ('.store-verify-' + [guid]::NewGuid().ToString('N'))
$priConfigPath = Join-Path $outputPath ('.store-priconfig-' + [guid]::NewGuid().ToString('N') + '.xml')

try {
    New-Item -ItemType Directory -Path $stagePath -Force | Out-Null
    Get-ChildItem -LiteralPath $clientPath -Force | Copy-Item -Destination $stagePath -Recurse -Force

    $strings = @{
        'en-US' = @{ DisplayName = 'YingView'; Description = 'A local-first image viewer for browsing and comparing pictures.' }
        'zh-CN' = @{ DisplayName = '映窗'; Description = '本地图片查看与对比工具。' }
    }
    foreach ($locale in @('en-US', 'zh-CN')) {
        $localeAssetPath = Join-Path $stagePath ("Assets\$locale")
        $localeStringPath = Join-Path $stagePath ("Strings\$locale")
        New-Item -ItemType Directory -Path $localeAssetPath, $localeStringPath -Force | Out-Null
        $sourceIcon = [Drawing.Bitmap]::new((Resolve-Path -LiteralPath $appIconPath).Path)
        try {
            $renderPng = {
                param([string]$FileName, [int]$Size)
                $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
                try {
                    $graphics = [Drawing.Graphics]::FromImage($bitmap)
                    try {
                        $graphics.Clear([Drawing.Color]::Transparent)
                        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
                        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
                        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                        $graphics.DrawImage($sourceIcon, [Drawing.Rectangle]::new(0, 0, $Size, $Size), [Drawing.Rectangle]::new(0, 0, 1024, 1024), [Drawing.GraphicsUnit]::Pixel)
                    }
                    finally { $graphics.Dispose() }
                    $bitmap.Save((Join-Path $localeAssetPath $FileName), [Drawing.Imaging.ImageFormat]::Png)
                }
                finally { $bitmap.Dispose() }
            }
            foreach ($asset in @(@('Square44x44Logo', 44), @('Square150x150Logo', 150), @('StoreLogo', 50))) {
                & $renderPng ($asset[0] + '.png') $asset[1]
                foreach ($scale in @(100, 200, 400)) {
                    $scaledSize = [int]($asset[1] * $scale / 100)
                    & $renderPng ("{0}.scale-{1}.png" -f $asset[0], $scale) $scaledSize
                }
            }
            foreach ($targetSize in @(16, 24, 32, 48, 256)) {
                & $renderPng ("Square44x44Logo.targetsize-{0}_altform-unplated.png" -f $targetSize) $targetSize
            }
        }
        finally { $sourceIcon.Dispose() }

        $displayNameXml = [Security.SecurityElement]::Escape($strings[$locale].DisplayName)
        $descriptionXml = [Security.SecurityElement]::Escape($strings[$locale].Description)
        $resw = @"
<?xml version="1.0" encoding="utf-8"?>
<root xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <data name="AppDisplayName" xml:space="preserve"><value>$displayNameXml</value></data>
  <data name="AppDescription" xml:space="preserve"><value>$descriptionXml</value></data>
</root>
"@
        [IO.File]::WriteAllText((Join-Path $localeStringPath 'Resources.resw'), $resw, [Text.UTF8Encoding]::new($false))
    }

    $xmlName = [Security.SecurityElement]::Escape($IdentityName)
    $xmlPublisher = [Security.SecurityElement]::Escape($Publisher)
    $xmlPublisherDisplay = [Security.SecurityElement]::Escape($PublisherDisplayName)
    $extensions = @('.png','.jpg','.jpeg','.gif','.webp','.avif','.bmp','.tif','.tiff','.ico','.heic','.heif')
    $fileTypes = ($extensions | ForEach-Object { "              <uap:FileType>$_</uap:FileType>" }) -join "`n"
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
         xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap uap3 desktop rescap">
  <Identity Name="$xmlName" Publisher="$xmlPublisher" Version="$Version" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>ms-resource:AppDisplayName</DisplayName>
    <PublisherDisplayName>$xmlPublisherDisplay</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources>
    <Resource Language="en-US" />
    <Resource Language="zh-CN" />
  </Resources>
  <Applications>
    <Application Id="YingView" Executable="YingView.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="ms-resource:AppDisplayName" Description="ms-resource:AppDescription" BackgroundColor="transparent"
                          Square44x44Logo="Assets\Square44x44Logo.png" Square150x150Logo="Assets\Square150x150Logo.png" />
      <Extensions>
        <desktop:Extension Category="windows.fullTrustProcess" Executable="YingView.exe">
          <desktop:FullTrustProcess />
        </desktop:Extension>
        <uap:Extension Category="windows.fileTypeAssociation" EntryPoint="Windows.FullTrustApplication" Executable="YingView.exe">
          <uap3:FileTypeAssociation Name="imagefiles" Parameters="&quot;%1&quot;" MultiSelectModel="Player">
            <uap:DisplayName>ms-resource:AppDisplayName</uap:DisplayName>
            <uap:Logo>Assets\Square44x44Logo.png</uap:Logo>
            <uap:SupportedFileTypes>
$fileTypes
            </uap:SupportedFileTypes>
          </uap3:FileTypeAssociation>
        </uap:Extension>
      </Extensions>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@
    $manifestPath = Join-Path $stagePath 'AppxManifest.xml'
    [IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))

    & $makePri createconfig /cf $priConfigPath /dq lang-en-US /pv 10.0.0 /o
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $priConfigPath -PathType Leaf)) {
        throw "MakePri could not create the default en-US resource configuration (exit code $LASTEXITCODE)."
    }
    # The SDK's default config splits language candidates into separate PRI files.
    # This product ships one MSIX, so keep all language candidates in its main PRI.
    [xml]$priConfig = Get-Content -LiteralPath $priConfigPath -Raw
    $packagingNode = $priConfig.SelectSingleNode('/resources/packaging')
    if ($null -ne $packagingNode) { [void]$priConfig.DocumentElement.RemoveChild($packagingNode) }
    $priConfig.Save($priConfigPath)
    & $makePri new /pr $stagePath /cf $priConfigPath /mn $manifestPath /in $IdentityName /of (Join-Path $stagePath 'resources.pri') /o
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $stagePath 'resources.pri') -PathType Leaf)) {
        throw "MakePri could not index the en-US and zh-CN resources (exit code $LASTEXITCODE)."
    }
    $stagePriFiles = @(Get-ChildItem -LiteralPath $stagePath -Filter '*.pri' -File -Recurse)
    if ($stagePriFiles.Count -ne 1 -or $stagePriFiles[0].Name -ne 'resources.pri') {
        throw 'The one-package build must contain exactly one PRI index with all language candidates.'
    }

    if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
    & $makeAppx pack /v /h SHA256 /d $stagePath /p $packagePath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "MakeAppx failed to create the package (exit code $LASTEXITCODE)."
    }

    & $makeAppx unpack /v /p $packagePath /d $verifyPath
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx could not unpack the package for verification (exit code $LASTEXITCODE)." }
    [xml]$verifiedManifest = Get-Content -LiteralPath (Join-Path $verifyPath 'AppxManifest.xml') -Raw
    $ns = [Xml.XmlNamespaceManager]::new($verifiedManifest.NameTable)
    $ns.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $ns.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')
    $ns.AddNamespace('uap3', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/3')
    $actualIdentity = $verifiedManifest.SelectSingleNode('/f:Package/f:Identity', $ns)
    if ($actualIdentity.Name -ne $IdentityName -or $actualIdentity.Publisher -ne $Publisher -or $actualIdentity.Version -ne $Version) {
        throw 'Unpacked manifest identity differs from the requested Partner Center identity or version.'
    }
    if ($verifiedManifest.SelectSingleNode('/f:Package/f:Properties/f:DisplayName', $ns).InnerText -ne 'ms-resource:AppDisplayName') {
        throw 'Package display name is not localized through the resource index.'
    }
    $actualTypes = @($verifiedManifest.SelectNodes('//uap3:FileTypeAssociation/uap:SupportedFileTypes/uap:FileType', $ns) | ForEach-Object { $_.InnerText })
    if ($actualTypes.Count -ne $extensions.Count -or @($extensions | Where-Object { $_ -notin $actualTypes }).Count -gt 0) {
        throw 'Unpacked manifest file associations are incomplete.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $verifyPath 'resources.pri') -PathType Leaf)) { throw 'The package does not contain resources.pri.' }
    foreach ($locale in @('en-US', 'zh-CN')) {
        $reswPath = Join-Path $verifyPath ("Strings\$locale\Resources.resw")
        $displayNamePath = Join-Path $verifyPath ("Assets\$locale\Square44x44Logo.png")
        $tilePath = Join-Path $verifyPath ("Assets\$locale\Square150x150Logo.png")
        $storeLogoPath = Join-Path $verifyPath ("Assets\$locale\StoreLogo.png")
        foreach ($requiredPath in @($reswPath, $displayNamePath, $tilePath, $storeLogoPath)) {
            if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) { throw "Package verification did not find localized resource: $requiredPath" }
        }
    }
    $priDumpPath = Join-Path $verifyPath 'resources-dump.xml'
    & $makePri dump /if (Join-Path $verifyPath 'resources.pri') /of $priDumpPath /o
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $priDumpPath -PathType Leaf)) { throw "MakePri could not verify resources.pri (exit code $LASTEXITCODE)." }
    $priDump = Get-Content -LiteralPath $priDumpPath -Raw
    foreach ($expectedText in @('AppDisplayName', 'AppDescription', 'YingView', '映窗', 'Language-en-US', 'Language-zh-CN')) {
        if ($priDump -notmatch [Regex]::Escape($expectedText)) { throw "The generated PRI is missing an expected localized resource: $expectedText" }
    }

    Write-Output "Created unsigned multilingual Store package: $packagePath"
    Write-Output "Verified one package identity, en-US/zh-CN strings and icon resources, file associations, and PRI index with MakePri $makePri and MakeAppx $makeAppx"
    Write-Output 'This is a local unsigned package artifact; it has not been installed, signed, uploaded, or submitted.'
}
finally {
    $outputPrefix = [IO.Path]::GetFullPath($outputPath).TrimEnd('\') + '\'
    foreach ($temporaryPath in @($stagePath, $verifyPath, $priConfigPath)) {
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryPath)
        $leaf = [IO.Path]::GetFileName($resolvedTemporary)
        $expectedName = $leaf -match '^\.store-(stage|verify)-[a-f0-9]{32}$' -or $leaf -match '^\.store-priconfig-[a-f0-9]{32}\.xml$'
        if (-not $resolvedTemporary.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not $expectedName) {
            throw "Refusing cleanup outside the generated staging paths: $resolvedTemporary"
        }
        if (Test-Path -LiteralPath $resolvedTemporary) { Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force }
    }
}
