# App identity

YingView uses the same overlapping-image symbol in every language. The Chinese client is named 映窗; English and other-language clients are named YingView. Do not combine both names inside a single-language client or replace the symbol with localized initials.

The editable master is `src/YingView/Assets/app-icon.svg`. Its two blue image planes surround an open window. `app-mark.svg` is the generated single-color version for print and monochrome use. Keep the opening clear and preserve the silhouette and proportions.

Run `dotnet run --project tools/YingView.IconBuilder -c Release` on Windows to regenerate the assets. `scripts/make-icon.py` forwards to the same builder; set `DOTNET_PATH` when using a portable SDK. The builder reads the SVG geometry and gradients, exports a 1024×1024 RGBA PNG and ten-size ICO files, and saves light/dark small-size previews under `output/branding`.

The three ICO resource filenames are retained for compatibility and contain identical artwork. Localized display names, installer metadata and shortcuts remain language-specific. macOS builds consume the 1024px PNG directly for their iconsets, rather than enlarging a 256px ICO. Store packaging uses this same master for scaled and target-size resources.

Change the SVG master first, regenerate all outputs, review actual 16/24/32/48px artwork on light and dark backgrounds, and rebuild both language clients together. Do not use a cropped concept-board raster as an application icon.
