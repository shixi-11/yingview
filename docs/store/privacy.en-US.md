# Privacy information for the Microsoft Store

## Data handled by the app

YingView opens image files selected by the user from the local device. It does not upload image content, use advertising, or send usage analytics.

To provide the recent-images menu, YingView stores up to eight recently opened file paths in the local preferences file at `%LOCALAPPDATA%\YingView\settings.json`. The file also stores language, viewing, theme, text-size, and toolbar preferences.

The image-comparison window keeps “Keep” and “Pending” marks for the current session. If the user exports the marks, YingView creates a CSV file at the location the user chooses. The export can contain image file names and paths. Neither the marks nor the CSV are sent by the app.

When the user invokes **Delete** or **Del** for the current image, YingView asks Windows to move that file to the Recycle Bin. This is supported on local fixed drives; network and removable drives are unsupported. Viewing, comparing, rotating, and mirroring do not change image bytes.

When an unhandled application error occurs, YingView may write diagnostic details to `%LOCALAPPDATA%\YingView\errors.log`. These details can include file paths or other context included in an exception. The files remain on the device and are not sent to YingView or a server by the app. The user can remove local YingView data through Windows File Explorer.

## Data sharing and retention

The app does not transmit images, preferences, comparison marks, CSV exports, or diagnostic details to the developer or third parties. Local preferences and diagnostic files remain until the user clears or removes them. CSV exports remain wherever the user saves them.

In Store editions with purchasing enabled, Microsoft Store handles product, purchase, and license requests under Microsoft's privacy terms. YingView does not receive payment-card details. Purchasing is not enabled in the current preview.

## Contact

For support and privacy questions, use [GitHub Issues](https://github.com/shixi-11/yingview/issues). Please keep private images, file paths, and payment information out of public reports.
