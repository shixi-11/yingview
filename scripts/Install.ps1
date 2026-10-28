# Optional per-user installation. No administrator privileges or default-app changes.
param([string]$Source = $PSScriptRoot, [switch]$KeepRunningVersion, [switch]$RestartRunningVersion, [string]$OpenPath)
$ErrorActionPreference = 'Stop'
$client = Join-Path $Source 'YingView.exe'
if (-not (Test-Path -LiteralPath $client)) { throw 'Place this script beside the published YingView.exe.' }
$manifestPath = Join-Path $Source 'client-language.json'
$manifest = $null
$language = 'en-US'
$displayName = 'YingView'
if (Test-Path -LiteralPath $manifestPath) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.Language -in @('zh-CN','en-US')) { $language = $manifest.Language }
    if ($manifest.Language -eq 'auto') { $language = if ([Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName -eq 'zh') { 'zh-CN' } else { 'en-US' } }
}
$displayName = if ($language -eq 'zh-CN') { '映窗' } else { 'YingView' }
$installRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "Programs/YingView-$language"))
$destination = $installRoot
$version = if ($manifest) { [string]$manifest.Version } else { '' }
if (($KeepRunningVersion -or $RestartRunningVersion) -and $version -notmatch '^\d+\.\d+\.\d+$') { throw 'A valid client version is required for a side-by-side update.' }
$switchProcesses = @()
if ($RestartRunningVersion) {
    $rootPrefix = $installRoot.TrimEnd('\') + '\'
    foreach ($process in Get-Process -Name YingView -ErrorAction SilentlyContinue) {
        $processPath = $null
        try {
            $processPath = [IO.Path]::GetFullPath($process.Path)
        } catch { }
        if (-not $processPath -or -not $processPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
        $sortVersion = [Version]'0.0.0'
        try {
            $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($processPath).ProductVersion
            $versionMatch = [regex]::Match([string]$productVersion, '^(\d+\.\d+\.\d+)')
            if ($versionMatch.Success) { $sortVersion = [Version]$versionMatch.Groups[1].Value }
        } catch { }
        $switchProcesses += [pscustomobject]@{ Process = $process; Version = $sortVersion }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    foreach ($entry in ($switchProcesses | Sort-Object Version, @{ Expression = { $_.Process.Id } })) {
        $process = $entry.Process
        if ($process.HasExited) { continue }
        try { [void]$process.CloseMainWindow() } catch { }
        while ([DateTime]::UtcNow -lt $deadline -and -not $process.HasExited) { Start-Sleep -Milliseconds 100 }
    }
    $remainingProcesses = @($switchProcesses | Where-Object { -not $_.Process.HasExited })
    if ($remainingProcesses.Count -gt 0) {
        $destination = Join-Path $installRoot "versions/$version"
    }
}
if ($KeepRunningVersion) {
    $destination = Join-Path $installRoot "versions/$version"
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$installed = Join-Path $destination 'YingView.exe'
$exeSourcePath = [IO.Path]::GetFullPath($client)
$exeDestinationPath = [IO.Path]::GetFullPath($installed)
if ($exeSourcePath -ne $exeDestinationPath) {
    try {
        Copy-Item -LiteralPath $client -Destination $installed -Force
    } catch {
        if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The installed executable is busy and no valid version is available for a side-by-side update.' }
        $buildHash = (Get-FileHash -LiteralPath $client -Algorithm SHA256).Hash.Substring(0, 8).ToLowerInvariant()
        $destination = Join-Path $installRoot "versions/$version-$buildHash"
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        $installed = Join-Path $destination 'YingView.exe'
        try {
            Copy-Item -LiteralPath $client -Destination $installed -Force
        } catch {
            $destination = Join-Path $installRoot "versions/$version-$([guid]::NewGuid().ToString('N'))"
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            $installed = Join-Path $destination 'YingView.exe'
            Copy-Item -LiteralPath $client -Destination $installed -Force
        }
    }
}
foreach ($file in @('LICENSE','NOTICE','CLIENT-LICENSE.txt','CLIENT-LICENSE.zh-CN.txt','LICENSING.md','THIRD-PARTY-NOTICES.md')) {
    $from = Join-Path $Source $file
    $to = Join-Path $destination $file
    if ((Test-Path -LiteralPath $from) -and ([IO.Path]::GetFullPath($from) -ne [IO.Path]::GetFullPath($to))) { Copy-Item -LiteralPath $from -Destination $to -Force }
}
if (Test-Path -LiteralPath $manifestPath) { Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $destination 'client-language.json') -Force }
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
$pinnedFolder = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'
if (Test-Path -LiteralPath $pinnedFolder) {
    $rootPrefix = $installRoot.TrimEnd('\') + '\'
    foreach ($shortcutPath in Get-ChildItem -LiteralPath $pinnedFolder -Filter '*.lnk' -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName) {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        try { $targetPath = [IO.Path]::GetFullPath($shortcut.TargetPath) } catch { continue }
        if (-not $targetPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
        $shortcut.TargetPath = $installed
        $shortcut.WorkingDirectory = $destination
        $shortcut.IconLocation = "$installed,0"
        $shortcut.Save()
        [YingViewShortcutIdentity]::Set($shortcutPath, $desktopAppId)
    }
}
# The installed client owns registration so installer and in-app setup cannot diverge.
$registration = Start-Process -FilePath $installed -ArgumentList '--register-installed-client' -WindowStyle Hidden -PassThru
if (-not $registration.WaitForExit(30000)) { throw 'Client registration did not finish within 30 seconds. Previous versions were retained.' }
if ($registration.ExitCode -ne 0) { throw 'The installed client could not register its image-opening command.' }
$expectedCommand = '"' + $installed + '" "%1"'
foreach ($subkey in @("Software\Classes\YingView.$language\shell\open\command", 'Software\Classes\Applications\YingView.exe\shell\open\command')) {
    $registeredKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subkey)
    try {
        if (-not $registeredKey -or $registeredKey.GetValue('') -cne $expectedCommand) {
            throw 'Image-opening registration did not point to the installed client. Previous versions were retained.'
        }
    } finally { if ($registeredKey) { $registeredKey.Dispose() } }
}
[YingViewShortcutIdentity]::NotifyAssociationsChanged()
# Shortcuts and registration now point to the new client, so older side-by-side copies are removed unless a window still runs from them.
$versionsRoot = Join-Path $installRoot 'versions'
if (Test-Path -LiteralPath $versionsRoot) {
    $runningPaths = @(Get-Process -Name YingView -ErrorAction SilentlyContinue | ForEach-Object { try { [IO.Path]::GetFullPath($_.Path) } catch { } })
    $current = [IO.Path]::GetFullPath($destination).TrimEnd('\')
    foreach ($old in Get-ChildItem -LiteralPath $versionsRoot -Directory) {
        $oldPath = $old.FullName.TrimEnd('\')
        if ($oldPath -eq $current) { continue }
        if ($runningPaths | Where-Object { $_.StartsWith($oldPath + '\', [StringComparison]::OrdinalIgnoreCase) }) { continue }
        try { Remove-Item -LiteralPath $oldPath -Recurse -Force } catch { }
    }
    if (-not (Get-ChildItem -LiteralPath $versionsRoot -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $versionsRoot -Force }
}
if ($RestartRunningVersion) {
    if ($remainingProcesses.Count -eq 0) {
        $startArguments = @{}
        if (-not [string]::IsNullOrWhiteSpace($OpenPath)) {
            if (-not (Test-Path -LiteralPath $OpenPath -PathType Leaf)) { throw 'OpenPath must identify an existing file.' }
            $argumentBuilder = New-Object System.Text.StringBuilder
            [void]$argumentBuilder.Append('"')
            $slashes = 0
            foreach ($character in $OpenPath.ToCharArray()) {
                if ($character -eq '\') { $slashes++; continue }
                if ($character -eq '"') {
                    [void]$argumentBuilder.Append(('\' * (2 * $slashes + 1))).Append('"')
                } else {
                    if ($slashes -gt 0) { [void]$argumentBuilder.Append(('\' * $slashes)) }
                    [void]$argumentBuilder.Append($character)
                }
                $slashes = 0
            }
            if ($slashes -gt 0) { [void]$argumentBuilder.Append(('\' * (2 * $slashes))) }
            [void]$argumentBuilder.Append('"')
            $startArguments.ArgumentList = $argumentBuilder.ToString()
        }
        Start-Process -FilePath $installed -WorkingDirectory $destination @startArguments
        if ($language -eq 'zh-CN') { Write-Output '映窗已更新，并已启动新版本。' } else { Write-Output 'YingView was updated and the new version was started.' }
    } else {
        if ($language -eq 'zh-CN') { Write-Output '映窗已更新；旧版本未能在5秒内退出，未切换正在运行的窗口。新版本已安装，下一次启动将使用新版本。' } else { Write-Output 'YingView was updated. The old version did not exit within 5 seconds, so its running windows were left unchanged. The new version is installed for the next launch.' }
    }
} elseif ($language -eq 'zh-CN') { Write-Output '映窗已安装。可在Windows“打开方式”中选择映窗。' } else { Write-Output 'YingView installed. Select YingView in Windows Open with to use it for images.' }
