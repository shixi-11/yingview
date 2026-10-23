# 映窗（YingView）

映窗是一款面向 Windows 10 和 11 的中英双语本地图片查看器。打开图片或拖入文件夹，就能在宽阔、清爽的窗口里浏览。应用使用 .NET 10 与 WPF 构建，图片解码由 Magick.NET 15 提供。

**版本：**0.1.0 Preview · **架构：**x64

[English](README.md) · [下载最新版本](https://github.com/shixi-11/yingview/releases) · [主题说明](docs/themes.md)

## 功能

- 默认以最大化窗口打开图片，按比例完整显示；小图也会自动放大。
- 提供三种显示方式：“完整显示”可看见整张图片；“铺满屏幕”会裁去边缘；“原始像素”按一个图片像素对应一个显示器物理像素显示。放大或裁切后可拖动画面。
- 滚轮围绕指针位置缩放。方向键可按文件名自然顺序浏览当前文件夹，也可打开缩略图栏。
- 动图受200帧、解码帧总量256 MiB和单张图片1亿像素限制。TIFF文件打开第一页。
- 提供简体中文和英文界面、四种内置风格，以及JSON主题导入和导出。

解码器已配置处理 PNG、JPEG、GIF、WebP、AVIF、BMP、TIFF、ICO、HEIC 和 HEIF 扩展名。能否打开仍取决于文件编码与内容；列出扩展名不代表保证每个文件都能成功解码。

## 图片处理

映窗用于查看图片。旋转和镜像只改变屏幕上的显示方式，不会编辑或保存对原图的修改。映窗不使用AI放大，也不宣称能恢复图片细节。图片在本机处理，不含广告，也不会上传图片。

## 下载与构建

0.1.0 Preview 版本尚未签名，映窗目前也尚未上架 Microsoft Store。可从 [GitHub Releases](https://github.com/shixi-11/yingview/releases) 下载 Windows x64 版本。

从源码构建需要安装 .NET 10 SDK，然后运行：

```powershell
.\scripts\build.ps1
```

## 许可

映窗采用 [PolyForm Noncommercial License 1.0.0](LICENSE)。商业用途须另行取得著作权人书面许可。第三方依赖遵循各自的许可证。

## 开发者

光之十一
