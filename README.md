# YingView

See the whole picture, then look closer. YingView is a free, open-source image viewer created by Shixi Lin. Open a picture or folder in a clean, maximized window, browse in natural filename order, and compare two images side by side. Use the photo grid to select multiple images, save favorites, move selections to the Recycle Bin and compare any two photos. Choose a black, white, gray, checkerboard or custom background to inspect artwork and transparent edges. Your images stay on your device.

[简体中文](README.zh-CN.md) · [Download](https://github.com/shixi-11/yingview/releases/latest) · [Report an issue](https://github.com/shixi-11/yingview/issues) · [Support development](https://shixilin.com/en/support)

**Windows source: 0.3.22 Preview · Windows 10/11 x64 · AGPL-3.0-only**

## Windows features

| What you want to do | What YingView offers |
| --- | --- |
| See the entire image | Opens maximized and enlarges small pictures proportionally. Fit keeps the whole picture visible; 100% shows physical image pixels. |
| Browse a folder | Open or drop images and folders, move in natural filename order, and expand the compact thumbnail strip when needed. |
| Inspect details | Zoom around the pointer, pan, rotate or mirror the view, show image information, and switch to fullscreen. |
| Check transparency | Switch the entire canvas between black, white, gray and checkerboard, or open the compact color palette beside the picture. Drag or enter a HEX color to preview it live; confirm to save, cancel to restore. |
| Compare alternatives | View two images side by side with synchronized zoom and panning, mark images Keep or Pending, and export session marks to a local CSV. |
| Keep everyday actions close | Copy an image or its path, paste an image, reopen recent files, and reveal a file in its folder. Delete/Del moves supported local files to the Windows Recycle Bin. |
| Make the interface yours | Four appearance presets, custom colors, text size, compact controls, and JSON theme import/export. |
| Watch moving images | Play or pause supported animated images, or browse a folder as a slideshow. |
| Keep the client current | Standalone update checks, an installation helper and a default-image-viewer setup flow. |

The palette is embedded in the main window, so it does not cover the image with a separate dialog. Fit mode adjusts the picture to the remaining space. The Background button cycles four presets; **B** switches black/white. **T** toggles thumbnails, **F** fits the image, **1** selects actual pixels, **V** opens comparison, and **F11** toggles fullscreen.

### Photo selection for photographers and clients

Click **Select photos**, or press **Ctrl+Shift+P**, to open the contact sheet. Choose **Open folder** to load a whole set, or drop a folder or several images onto the grid. **Ctrl-click** selects individual photos; **Shift-click** selects a range. Select exactly two images and choose **Compare two** for side-by-side inspection with synchronized zoom and panning. Double-click a photo for a whole-image preview; close the preview to return to the grid.

Favorites are saved locally between sessions. Filter to favorites or export their filenames as a TXT list. You can move multiple selected files to the Recycle Bin after one confirmation; failed files remain in the selection, with no permanent-delete fallback. The grid shows up to 120 thumbnails per page and supports selections across pages.

The same selection interface works for photographers and clients. Favorites stay on the current device; export the filename list for handoff. This release does not include a dedicated RAW workflow, star ratings, photo editing or cloud delivery.

### Formats and practical limits

PNG, JPEG, GIF, WebP, AVIF, BMP, TIFF and ICO are supported. TIFF opens at its first page. HEIC/HEIF extensions are recognized, but their compatibility has not been verified for this release.

Animations are limited to 200 frames and images to 100 million pixels. Larger decoded animations can use temporary disk-backed frames rather than keeping all frames in RAM; 256 MiB is the memory threshold, not a blanket file-size limit. Temporary decoded frames are capped at 2 GiB, and decoder resource limits can reject demanding files earlier. There is no promise that every encoding or animation will open.

Recycle Bin deletion is supported for local fixed drives. Network and removable drives are not supported by that command.

## Language and installation

Download **YingView-en-US.exe** for the English client or **YingView-zh-CN.exe** for the Simplified Chinese client. The chosen build determines the interface, display name, icon and installed shortcut language. Other interface languages currently fall back to English.

Run the downloaded executable to start viewing. For an installed shortcut and file-association registration, use the installation helper supplied by a local build. The default-viewer setup reports which supported formats Windows actually associates with YingView.

Updates are checked at most once per day after the first window appears, if enabled. Download and installation require your choice. Store-managed clients use the Store update path. See the [update guide](docs/updates.md).

## macOS preview

The repository also includes a native macOS 13+ client built with SwiftUI, AppKit and ImageIO. It supports image/folder opening, fit and physical-pixel views, browsing, zoom, rotation, Trash, thumbnails, appearance settings, a photo selection grid, Cmd/Shift multi-selection, batch Trash and comparison of any two selected photos. It does not yet include every Windows feature listed above.

The build script produces separate Chinese and English apps for Apple silicon and Intel. Developer ID signing, notarization and Mac App Store submission are pending. Check the actual release assets before downloading.

## Privacy

Viewing, rotating and mirroring do not overwrite original image files. YingView does not upload image content, show ads or send usage analytics. Preferences and recent paths stay local; large animations may create temporary decoded frames, and comparison marks are exported only when you choose to save them. Optional support links open your browser. See the [privacy information](docs/store/privacy.en-US.md).

## Build from source

Windows uses .NET 10, WPF and Magick.NET 15. With the .NET 10 SDK installed, run:

```powershell
.\scripts\build.ps1
```

The script builds the English and Simplified Chinese standalone clients. macOS build instructions are in [src/YingView.macOS](src/YingView.macOS). Microsoft Store packaging materials are in [docs/store](docs/store); the app has not yet been submitted.

## License, support and collaboration

From **0.3.21**, original YingView code and official clients use **GNU AGPLv3 only (`AGPL-3.0-only`)**. Personal and commercial use, modification and redistribution are permitted under its terms, including the applicable source-availability obligations. See [LICENSE](LICENSE) and the [licensing overview](LICENSING.md). Earlier releases and third-party components retain their respective terms.

YingView is free to use. If it helps your work, [optional donations](https://shixilin.com/en/support) support continued maintenance. All features remain available without donating.

Created by **Shixi Lin**. Collaboration: [info@elevencapital.ltd](mailto:info@elevencapital.ltd).
