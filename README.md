# YingView

YingView is a bilingual, local-first image viewer for Windows 10 and 11. Open a picture or drop in a folder, then browse in a large, uncluttered window. The app is built with .NET 10 and WPF, with image decoding provided by Magick.NET 15.

**Version:** 0.1.0 preview · **Architecture:** x64

[简体中文](README.zh-CN.md) · [Download the latest release](https://github.com/shixi-11/yingview/releases) · [Theme guide](docs/themes.md)

## What it does

- Opens images in a maximized window and fits them to the available view. Small images are enlarged while keeping their proportions.
- Offers three viewing modes: Fit shows the whole image, Fill covers the window and crops the edges, and Actual Pixels maps one image pixel to one physical display pixel. Drag to pan a zoomed or cropped image.
- Zooms around the mouse pointer. Browse the current folder in natural filename order with the arrow keys, or use the optional thumbnail strip.
- Supports animated images within limits of 200 frames, 256 MiB of decoded frames, and 100 million pixels per image. TIFF files open at their first page.
- Includes Chinese and English interface text, four built-in appearance presets, and JSON theme import and export.

The decoder is configured for PNG, JPEG, GIF, WebP, AVIF, BMP, TIFF, ICO, HEIC, and HEIF extensions. Decoding depends on each file's encoding and contents; this list does not guarantee that every file will open.

## Image handling

YingView is for viewing. Rotate and mirror change the on-screen view only; the app does not edit or save changes to your original image. It does not use AI upscaling or claim to restore image detail. Images are processed locally, with no ads and no image uploads.

## Download and build

The 0.1.0 preview build is unsigned and YingView does not yet have a Microsoft Store listing. Download the Windows x64 build from [GitHub Releases](https://github.com/shixi-11/yingview/releases).

To build from source, install the .NET 10 SDK and run:

```powershell
.\scripts\build.ps1
```

## License

YingView is provided under the [PolyForm Noncommercial License 1.0.0](LICENSE). Commercial use requires separate written permission from the copyright holder. Third-party dependencies are governed by their own licenses.

## Creator

Shixi Lin
