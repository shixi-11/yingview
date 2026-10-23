# Optional per-user installation. No administrator privileges or default-app changes.
param([string]$Source = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$client = Join-Path $Source 'YingView.exe'
if (-not (Test-Path -LiteralPath $client)) { throw 'Place this script beside the published YingView.exe.' }
$destination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/YingView'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($file in @('YingView.exe','LICENSE','NOTICE','THIRD-PARTY-NOTICES.md')) {
    $from = Join-Path $Source $file
    $to = Join-Path $destination $file
    if ((Test-Path -LiteralPath $from) -and ([IO.Path]::GetFullPath($from) -ne [IO.Path]::GetFullPath($to))) { Copy-Item -LiteralPath $from -Destination $to -Force }
}
$installed = Join-Path $destination 'YingView.exe'
if (Test-Path -LiteralPath (Join-Path $Source 'ThirdParty')) { Copy-Item -LiteralPath (Join-Path $Source 'ThirdParty') -Destination $destination -Recurse -Force }
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'YingView.lnk'))
    $shortcut.TargetPath = $installed
    $shortcut.WorkingDirectory = $destination
    $shortcut.IconLocation = "$installed,0"
    $shortcut.Save()
}
$key = 'HKCU:\Software\Classes\Applications\YingView.exe'
New-Item -Path "$key\shell\open\command" -Force | Out-Null
Set-Item -Path "$key\shell\open\command" -Value ('"' + $installed + '" "%1"')
New-Item -Path "$key\SupportedTypes" -Force | Out-Null
foreach ($ext in @('.png','.jpg','.jpeg','.gif','.webp','.avif','.bmp','.tif','.tiff','.ico','.heic','.heif')) {
    New-ItemProperty -Path "$key\SupportedTypes" -Name $ext -Value '' -PropertyType String -Force | Out-Null
}
Write-Output 'YingView installed. Select it in Windows Open with to use it for images.'
