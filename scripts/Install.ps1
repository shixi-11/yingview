# Optional per-user installation. No administrator privileges or default-app changes.
param([string]$Source = $PSScriptRoot, [switch]$KeepRunningVersion)
$ErrorActionPreference = 'Stop'
$client = Join-Path $Source 'YingView.exe'
if (-not (Test-Path -LiteralPath $client)) { throw 'Place this script beside the published YingView.exe.' }
$manifestPath = Join-Path $Source 'client-language.json'
$language = 'en-US'
$displayName = 'YingView'
if (Test-Path -LiteralPath $manifestPath) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.Language -in @('zh-CN','en-US')) { $language = $manifest.Language }
    if ($manifest.Language -eq 'auto') { $language = if ([Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName -eq 'zh') { 'zh-CN' } else { 'en-US' } }
}
$displayName = if ($language -eq 'zh-CN') { '映窗' } else { 'YingView' }
$destination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "Programs/YingView-$language"
if ($KeepRunningVersion) {
    $version = if ($manifest) { [string]$manifest.Version } else { '' }
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'A valid client version is required for a side-by-side update.' }
    $destination = Join-Path $destination "versions/$version"
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($file in @('YingView.exe','LICENSE','NOTICE','THIRD-PARTY-NOTICES.md')) {
    $from = Join-Path $Source $file
    $to = Join-Path $destination $file
    if ((Test-Path -LiteralPath $from) -and ([IO.Path]::GetFullPath($from) -ne [IO.Path]::GetFullPath($to))) { Copy-Item -LiteralPath $from -Destination $to -Force }
}
if (Test-Path -LiteralPath $manifestPath) { Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $destination 'client-language.json') -Force }
$installed = Join-Path $destination 'YingView.exe'
if (Test-Path -LiteralPath (Join-Path $Source 'ThirdParty')) { Copy-Item -LiteralPath (Join-Path $Source 'ThirdParty') -Destination $destination -Recurse -Force }
$shell = New-Object -ComObject WScript.Shell
$shortcutIdentitySource = Join-Path $PSScriptRoot 'ShortcutIdentity.cs'
if (-not (Test-Path -LiteralPath $shortcutIdentitySource)) { throw 'ShortcutIdentity.cs must be beside Install.ps1.' }
if (-not ('YingViewShortcutIdentity' -as [type])) { Add-Type -Path $shortcutIdentitySource }
$desktopAppId = if ($language -eq 'zh-CN') { 'Shixi.YingView.ZhCN' } else { 'Shixi.YingView.EnUS' }
foreach ($folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
    $shortcutPath = Join-Path $folder "$displayName.lnk"
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $installed
    $shortcut.WorkingDirectory = $destination
    $shortcut.IconLocation = "$installed,0"
    $shortcut.Save()
    [YingViewShortcutIdentity]::Set($shortcutPath, $desktopAppId)
}
$key = "HKCU:\Software\Classes\YingView.$language"
New-Item -Path $key -Force | Out-Null
Set-Item -Path $key -Value $displayName
New-ItemProperty -Path $key -Name 'FriendlyAppName' -Value $displayName -PropertyType String -Force | Out-Null
New-ItemProperty -Path $key -Name 'AppUserModelID' -Value $desktopAppId -PropertyType String -Force | Out-Null
New-Item -Path "$key\DefaultIcon" -Force | Out-Null
Set-Item -Path "$key\DefaultIcon" -Value ('"' + $installed + '",0')
New-Item -Path "$key\shell\open\command" -Force | Out-Null
Set-Item -Path "$key\shell\open\command" -Value ('"' + $installed + '" "%1"')
$appKey = 'HKCU:\Software\Classes\Applications\YingView.exe'
New-Item -Path $appKey -Force | Out-Null
New-ItemProperty -Path $appKey -Name 'FriendlyAppName' -Value $displayName -PropertyType String -Force | Out-Null
New-Item -Path "$appKey\DefaultIcon" -Force | Out-Null
Set-Item -Path "$appKey\DefaultIcon" -Value ('"' + $installed + '",0')
New-Item -Path "$appKey\shell\open\command" -Force | Out-Null
Set-Item -Path "$appKey\shell\open\command" -Value ('"' + $installed + '" "%1"')
New-Item -Path "$appKey\SupportedTypes" -Force | Out-Null
foreach ($ext in @('.png','.jpg','.jpeg','.gif','.webp','.avif','.bmp','.tif','.tiff','.ico')) {
    New-ItemProperty -Path "$appKey\SupportedTypes" -Name $ext -Value '' -PropertyType String -Force | Out-Null
    $openWith = "HKCU:\Software\Classes\$ext\OpenWithProgids"
    New-Item -Path $openWith -Force | Out-Null
    New-ItemProperty -Path $openWith -Name "YingView.$language" -Value ([byte[]]@()) -PropertyType None -Force | Out-Null
}
$capabilityPath = "Software\YingView\$language\Capabilities"
$capabilities = "HKCU:\$capabilityPath"
New-Item -Path "$capabilities\FileAssociations" -Force | Out-Null
New-ItemProperty -Path $capabilities -Name ApplicationName -Value $displayName -PropertyType String -Force | Out-Null
New-ItemProperty -Path $capabilities -Name ApplicationIcon -Value ('"' + $installed + '",0') -PropertyType String -Force | Out-Null
New-ItemProperty -Path $capabilities -Name ApplicationDescription -Value $(if ($language -eq 'zh-CN') { '本地图片查看与对照审阅' } else { 'Local image viewing and comparison' }) -PropertyType String -Force | Out-Null
foreach ($ext in @('.png','.jpg','.jpeg','.gif','.webp','.avif','.bmp','.tif','.tiff','.ico')) {
    New-ItemProperty -Path "$capabilities\FileAssociations" -Name $ext -Value "YingView.$language" -PropertyType String -Force | Out-Null
}
New-Item -Path 'HKCU:\Software\RegisteredApplications' -Force | Out-Null
New-ItemProperty -Path 'HKCU:\Software\RegisteredApplications' -Name $displayName -Value $capabilityPath -PropertyType String -Force | Out-Null
if (-not ('YingViewShellNotification' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class YingViewShellNotification {
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
'@
}
[YingViewShellNotification]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
if ($language -eq 'zh-CN') { Write-Output '映窗已安装。可在Windows“打开方式”中选择映窗。' } else { Write-Output 'YingView installed. Select YingView in Windows Open with to use it for images.' }
