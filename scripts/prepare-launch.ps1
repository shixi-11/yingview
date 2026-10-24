[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$DownloadUrl,
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'output\launch')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^v?\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
    throw 'Version must be a simple semantic version such as v0.2.0-preview.1.'
}
$versionSlug = $Version.TrimStart('v')
$tag = if ($Version.StartsWith('v', [StringComparison]::OrdinalIgnoreCase)) { $Version } else { "v$Version" }

# Accept only the repository's versioned GitHub Release page or a direct release asset URL.
# No query, fragment, credentials, alternate host, or Markdown-sensitive characters are permitted.
$urlPattern = '^https://github\.com/shixi-11/yingview/releases/(?:tag/(?<tag>v?\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?)|download/(?<assetTag>v?\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?)/[A-Za-z0-9][A-Za-z0-9._+-]*\.(?:msix|exe|msi))$'
if ($DownloadUrl -notmatch $urlPattern) {
    throw 'DownloadUrl must be an https://github.com/shixi-11/yingview/releases/tag/<version> page or a direct .msix, .exe, or .msi release asset URL. Query strings and fragments are not accepted.'
}
$urlTag = if ($Matches.tag) { $Matches.tag } else { $Matches.assetTag }
if ($urlTag.TrimStart('v') -ne $versionSlug) {
    throw "DownloadUrl release tag '$urlTag' does not match Version '$Version'."
}

$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

$releaseZh = @"
# 映窗 $Version

映窗是一款面向本地图片浏览的 Windows 看图工具。

## 本版内容

- 图片按比例完整适配窗口；小图可自动放大，画面不会填满裁切。
- 可收起的迷你缩略图栏，便于浏览同一文件夹中的图片。
- 删除图片时送入 Windows 回收站。
- 主题预设支持即时预览，可调整颜色、文字大小和紧凑工具栏。
- 双图对比支持同步缩放和平移，可切换深色、浅色或棋盘格背景检查透明画面。
- “保留 / 待定”标记与 CSV 导出预览可免费使用。
- 图片留在本机处理，不会上传图片。
- 支持手动检查更新、新版提示，以及校验后的安装与重启。

## 获取版本

[查看此版本与下载文件]($DownloadUrl)

—— 光之十一
"@

$releaseEn = @"
# YingView $Version

YingView is a Windows image viewer for browsing local files.

## What's included

- Fit images to the window while preserving the full frame; small images can be enlarged without fill-cropping.
- A collapsible mini filmstrip for browsing images in the same folder.
- Deleted images are sent to the Windows Recycle Bin.
- Live-preview theme presets, with controls for colors, text size, and a compact toolbar.
- Two-image comparison with synchronized zoom and pan, plus dark, light, and checkerboard backgrounds for inspecting transparency.
- Keep / Pending labels and a free CSV export preview.
- Images stay on the device and are not uploaded.
- Check for updates, receive new-version notices, and install a verified update with a restart.

## Version and download

[View this release and its download files]($DownloadUrl)

— Shixi Lin
"@

$promotionZh = @"
# 中文发布草稿

以下内容是待审核文案。只有确认对应Release页面和下载文件已公开后，才发布带链接的版本。

## GitHub Release 简介

映窗 $Version 已准备好供本地图片浏览使用：完整适配窗口、小图自动放大、可收起缩略图栏、可送入回收站的删除，以及可调整的个人主题。双图比较支持同步缩放和平移，并提供深色、浅色与棋盘格背景；“保留 / 待定”与 CSV 导出预览可免费使用。图片不会上传。

[查看版本]($DownloadUrl)

## 中文设计师社群

大家好，我是光之十一。我做了一款专注于本地看图的 Windows 工具，想邀请首批8–12位设计师和创作者试用。它会让图片完整适配窗口，不用填满裁切；也可以并排比较两张图、同步缩放和平移，用深色、浅色与棋盘格背景检查透明素材。“保留 / 待定”标记和 CSV 导出预览目前免费提供。

如果你平时需要核对透明 PNG、图标或视觉稿，欢迎试用后告诉我哪一步最顺、哪里还卡手。图片不会上传，删除操作会送入 Windows 回收站。

[查看版本]($DownloadUrl)

## X

映窗 $Version 是我做的本地看图工具：完整适配窗口、小图可放大；双图同步缩放，棋盘底检查透明图。图片不上传。邀请8–12位设计师试用并反馈：

$DownloadUrl

光之十一
"@

$promotionEn = @"
# English launch drafts

These are review drafts. Publish linked copy only after confirming that the matching release page and download files are public.

## GitHub Release introduction

YingView $Version is ready for local image viewing: full-frame fit, automatic enlargement for small images, a collapsible filmstrip, Recycle Bin deletion, and adjustable personal themes. Compare two images with synchronized zoom and pan, inspect transparency on dark, light, or checkerboard backgrounds, and use Keep / Pending labels with a free CSV export preview. Images are not uploaded.

[View the release]($DownloadUrl)

## English-language designer community

Hi, I’m Shixi Lin. I’m inviting the first 8–12 designers and creators to try YingView, a Windows viewer for local images. It fits the whole image in the window, can enlarge smaller images without cropping, and lets you compare two images with synchronized zoom and pan. Dark, light, and checkerboard backgrounds help inspect transparent artwork. Keep / Pending labels and a CSV export preview are free to use.

If you review transparent PNGs, icons, or visual drafts, I’d value feedback on what feels smooth and where the workflow slows down. Images stay on your device; deleting one sends it to the Windows Recycle Bin.

[View the release]($DownloadUrl)

## X

YingView ${Version}: a Windows viewer for local files. Fit whole images, compare with synced zoom/pan, check transparency on checkerboards. No uploads. Seeking 8–12 testers:

$DownloadUrl

Shixi Lin
"@

$files = [ordered]@{
    "release-notes-$versionSlug.zh-CN.md" = $releaseZh
    "release-notes-$versionSlug.en-US.md" = $releaseEn
    "promotion-drafts-$versionSlug.zh-CN.md" = $promotionZh
    "promotion-drafts-$versionSlug.en-US.md" = $promotionEn
}
foreach ($entry in $files.GetEnumerator()) {
    $path = Join-Path $outputPath $entry.Key
    [IO.File]::WriteAllText($path, $entry.Value.Trim() + "`n", [Text.UTF8Encoding]::new($false))
    Write-Output $path
}
