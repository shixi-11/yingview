# Opening performance

When a supported image file is opened, the viewer decodes and presents that file before listing its directory. The list then restores natural-order sibling navigation. An inaccessible directory listing does not remove an otherwise readable image, and a late listing cannot replace a newer open request. Opening a directory still selects its first supported image in natural order.

Ordinary 8-bit PNG, RGB JPEG and BMP files use the Windows decoder at full resolution. Embedded ICC profiles, CMYK, EXIF rotation, high-depth images and animation controls retain the existing Magick pipeline. Decoded BGRA pixels are materialized before the stream closes and frozen for the UI thread. Format queries no longer initialize Magick's native runtime; its resource limits initialize when that decoder is actually needed. Thumbnails yield to the main image render.

Windows clients use an uncompressed self-contained single file to reduce repeated process startup work. This increases the executable size. It does not change image compression, resolution or output pixels.

## Local measurement scope

On the development Windows computer, three interleaved fresh-process runs per sample compared the previous decoder with the native path, using .NET 10 and Magick.NET 15.0.0. Four generated samples covered 3840×2160 and 4000×3000 PNG/JPEG. First-decode medians changed from 149–223 ms to 78–133 ms; all four full-resolution BGRA outputs matched byte for byte. Warm decode was measured separately. These figures measure the decoder call, not a guarantee of click-to-screen latency for arbitrary files or machines.

A separate single-file console startup probe measured approximately 359 ms with bundle compression and 218 ms without it, including process startup and two decodes. File sizes were approximately 83 MB and 193 MB respectively. This isolates the packaging tradeoff rather than measuring the full viewer's visible first frame.

Raw inputs, independent source snapshots, per-run CSVs and reproduction scripts are in the ignored `output/performance` directory. The app integration checks additionally hold the folder listing pending to verify that the requested image is already available, verify natural-order navigation afterward, and reject stale scan results. Image checks cover transparency, stream release, orientation, profiles, CMYK, animation routing and size limits.
