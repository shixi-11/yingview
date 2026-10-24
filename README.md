# YingView

YingView is a local-first image viewer for Windows 10 and 11. Open a picture or drop in a folder to browse it in a large, uncluttered window. It is built with .NET 10 and WPF, with image decoding provided by Magick.NET 15. Download the English build for the YingView name and English interface, or the Simplified Chinese build for the 映窗 name and Chinese interface.

**Version:** 0.3.6 Preview · **Architecture:** x64

[简体中文](README.zh-CN.md) · [Download from GitHub Releases](https://github.com/shixi-11/yingview/releases) · [Theme guide](docs/themes.md)

## What it does

- Opens images in a maximized window and fits the whole image to the available view. Small images are enlarged proportionally.
- Zooms around the mouse pointer and lets you pan a zoomed image. Use the arrow keys to browse the current folder in natural filename order.
- Keeps the controls compact and the thumbnail strip hidden by default; press **T** to show or hide the strip.
- Uses a solid black viewing background by default. Use the **◐** toolbar button or **B** to switch the whole viewing area between black and white; your choice is saved.
- Compares two images side by side with synchronized zoom and panning. Choose a dark, light, or checkerboard canvas, mark images to keep or review later, and export the session marks to a local CSV file.
- Sends the selected image to the Windows Recycle Bin with **Delete** or **Del**. This works for files on local fixed drives; network and removable drives are unsupported.
- Supports animated images within limits of 200 frames, 256 MiB of decoded frames, and 100 million pixels per image. TIFF files open at their first page.
- Includes four built-in appearance presets, live color and text-size adjustments, a compact-toolbar option, and free JSON theme import and export. The English build uses English interface text; the Simplified Chinese build uses Chinese interface text. Other languages use English.

The appearance editor now shows visual preset cards, color swatches, and a live text preview. Comparison controls follow the interface text-size setting. Empty folders, damaged files, and externally moved images clear the previous selection and offer a way to recover; **F5** refreshes the selected image or reopens its folder when it has moved.

In the standalone client, choose **More → Set as default image viewer**, then **Confirm and set all** to apply all 10 supported image formats together. The app checks the effective Windows associations and reports any formats that could not be changed. Packaged clients use the Windows Settings flow.

## macOS preview

The native client in `src/YingView.macOS` uses SwiftUI, AppKit, and the system ImageIO decoder on macOS 13 or later. It includes contain fit, physical-pixel 100%, navigation, zoom, rotation, Trash, a collapsible filmstrip, basic side-by-side viewing, and appearance settings. The build script produces separate Chinese and English apps with Apple silicon and Intel architectures. Developer ID signing and notarization remain pending; it has not been submitted to the Mac App Store.

The decoder is configured for PNG, JPEG, GIF, WebP, AVIF, BMP, TIFF, ICO, HEIC, and HEIF extensions. Support depends on each file's encoding and contents. HEIC and HEIF have not been verified in this release.

## Image handling and privacy

YingView opens and compares images locally. Viewing, rotating, and mirroring do not change image bytes. The app does not upload image content, include advertising, or send usage analytics. The only image-file action is the explicit Recycle Bin command described above. See the [privacy information](docs/store/privacy.en-US.md) for local data details.

## Download and build

This repository contains the 0.3.6 Preview source for Windows x64. Check the version on [GitHub Releases](https://github.com/shixi-11/yingview/releases) for the builds currently available to download; a source update does not automatically publish new download assets. The Microsoft Store submission has not been made.

Choose the download whose language matches your preference. The selected build also determines the app name, icon, and installed shortcut language.

To build from source, install the .NET 10 SDK and run:

```powershell
.\scripts\build.ps1
```

The generated `Install.ps1` can be run with `-KeepRunningVersion` to install an update alongside a running version and update the shortcuts. Reopen the app from its updated shortcut to use the new version.

## License

YingView is provided under the [PolyForm Noncommercial License 1.0.0](LICENSE). Commercial use requires separate written permission from the copyright holder. Third-party dependencies are governed by their own licenses.

## Creator

Shixi Lin
