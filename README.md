# YingView

YingView is a local-first image viewer for Windows 10 and 11. Open a picture or drop in a folder to browse it in a large, uncluttered window. It is built with .NET 10 and WPF, with image decoding provided by Magick.NET 15. Download the English build for the YingView name and English interface, or the Simplified Chinese build for the 映窗 name and Chinese interface.

**Version:** 0.2.0 Preview · **Architecture:** x64

[简体中文](README.zh-CN.md) · [Download from GitHub Releases](https://github.com/shixi-11/yingview/releases) · [Theme guide](docs/themes.md)

## What it does

- Opens images in a maximized window and fits the whole image to the available view. Small images are enlarged proportionally.
- Zooms around the mouse pointer and lets you pan a zoomed image. Use the arrow keys to browse the current folder in natural filename order.
- Keeps the controls compact and the thumbnail strip hidden by default; press **T** to show or hide the strip.
- Compares two images side by side with synchronized zoom and panning. Choose a dark, light, or checkerboard canvas, mark images to keep or review later, and export the session marks to a local CSV file.
- Sends the selected image to the Windows Recycle Bin with **Delete** or **Del**. This works for files on local fixed drives; network and removable drives are unsupported.
- Supports animated images within limits of 200 frames, 256 MiB of decoded frames, and 100 million pixels per image. TIFF files open at their first page.
- Includes four built-in appearance presets, live color and text-size adjustments, a compact-toolbar option, and free JSON theme import and export. The English build uses English interface text; the Simplified Chinese build uses Chinese interface text. Other languages use English.

The decoder is configured for PNG, JPEG, GIF, WebP, AVIF, BMP, TIFF, ICO, HEIC, and HEIF extensions. Support depends on each file's encoding and contents. HEIC and HEIF have not been verified in this release.

## Image handling and privacy

YingView opens and compares images locally. Viewing, rotating, and mirroring do not change image bytes. The app does not upload image content, include advertising, or send usage analytics. The only image-file action is the explicit Recycle Bin command described above. See the [privacy information](docs/store/privacy.en-US.md) for local data details.

## Download and build

This repository describes the 0.2.0 Preview for Windows x64. Check [GitHub Releases](https://github.com/shixi-11/yingview/releases) for the builds currently available to download. The Microsoft Store submission has not been made.

Choose the download whose language matches your preference. The selected build also determines the app name, icon, and installed shortcut language.

To build from source, install the .NET 10 SDK and run:

```powershell
.\scripts\build.ps1
```

## License

YingView is provided under the [PolyForm Noncommercial License 1.0.0](LICENSE). Commercial use requires separate written permission from the copyright holder. Third-party dependencies are governed by their own licenses.

## Creator

Shixi Lin
