# Windows updates

The standalone client checks the official repository's published Windows releases in the background, after its first window appears and at most once per day. The menu provides **Check for updates** and an automatic-check toggle. Network failures do not interrupt image viewing.

Only stable releases with an executable for the client's language are offered. Drafts, previews, macOS-only releases and older versions are excluded. The download follows GitHub's release CDN, has a size limit and must match the release asset's SHA-256 digest. The downloaded file is checked again before execution. Missing or mismatched verification data stops installation.

After the user selects **Install and restart**, the new binary extracts its own installer and license files, requests old viewer windows to close normally, replaces the per-user installation, refreshes shortcuts and registration, removes older side-by-side copies and reopens the selected picture. If an old process cannot close, it is left running, the new version is installed beside it for the next launch and the installer reports that the switch did not complete; that copy is replaced by the next update. No force termination is used. The installer runs without administrator access; its PowerShell execution-policy override applies to that installer process only.

Microsoft Store packages use Store updates. They do not download or install standalone binaries.

For local development delivery, build both language variants and install the Chinese client with:

```powershell
./scripts/build.ps1 -DotnetPath 'A:\vibe coding\.tools\dotnet10\dotnet.exe'
./scripts/Install.ps1 -Source ./dist/YingView-zh-CN -RestartRunningVersion
```

Check the actual running executable version, shortcut target, image-opening command and visible icon after installation. A successful build or a copied executable alone does not confirm that the user's running window has changed.
