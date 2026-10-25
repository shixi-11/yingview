# Microsoft Store submission checklist

## Before packaging

- [ ] The Store accepts packaged desktop apps, including WPF. This workflow submits compiled application files and metadata; it does not require publishing the source code or changing the project's license. See [Microsoft's desktop app packaging guide](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-packaging-dot-net).
- [ ] Reserve the final product name and package identity in Microsoft Partner Center. The chosen identity must match the package's `IdentityName` exactly.
- [ ] Copy the exact package `Publisher` value from Partner Center. Do not substitute the public-facing publisher display name.
- [ ] Confirm the public product name and publisher display name. The current script parameters can be changed for the final brand.
- [ ] Publish and verify working HTTPS pages for support, the app privacy policy, and the client EULA; enter their real URLs in Partner Center. The privacy page must identify YingView and explain local data handling. The EULA must be the separate free personal/commercial client license, not the PolyForm source license.
- [ ] Complete the free price and availability settings, app category, capabilities, and age-rating questionnaire in Partner Center.
- [ ] Prepare localized en-US and zh-CN listing metadata and screenshots. At least one screenshot is required; desktop screenshots must be PNG and at least 1366 x 768. The package/store logo requirements must also be met.
- [ ] Build the Release standalone client and confirm `ClientDirectory` contains `YingView.exe`, the official source `LICENSE`, `NOTICE`, `THIRD-PARTY-NOTICES.md`, `CLIENT-LICENSE.txt`, `CLIENT-LICENSE.zh-CN.txt`, `LICENSING.md`, and the complete published runtime files.
- [ ] Choose a four-part package version ending in `.0` that matches the app release (the current app version is `0.3.15`, so its package version is `0.3.15.0`).

## Create and inspect the package

Run PowerShell from the repository root only after reserving the app. Replace all bracketed identity and publisher placeholders with the exact values from Partner Center; the packaging script rejects placeholders and must not be run with a guessed identity:

First build the universal Store client with `./scripts/build.ps1 -ClientLanguage auto`. Its `client-language.json` must say `auto`; language-specific downloads are not Store packaging input. Localized names and icons are generated into one PRI index; see [localization](localization.md).

```powershell
& .\scripts\package-store.ps1 `
  -IdentityName '<reserved identity name>' `
  -Publisher '<exact Partner Center publisher string>' `
  -PublisherDisplayName '<public publisher name>' `
  -Version '0.3.15.0' `
  -ClientDirectory '.\dist\YingView-auto' `
  -OutputDirectory '.\output\store'
```

The script creates an unsigned x64 `.msix` package, then uses MakeAppx to unpack it and check the embedded identity, image associations, and icon assets. It does not sign, install, upload, or submit the package. Store distribution packages are re-signed by Microsoft after certification; use the actual Partner Center identity when building.

The current script emits a raw `.msix`. Microsoft recommends an `.msixupload` package for Store submission; use Visual Studio's packaging wizard or another supported packaging workflow to create the upload file and symbols if Partner Center requests that format.

## Local checks

- [x] Windows SDK packaging tools are available locally: `MakeAppx.exe` and `MakePri.exe` from SDK 10.0.26100.0 (x64).
- [ ] Run the Windows App Certification Kit (WACK) on a Release package when available as a recommended local pre-submission check. Microsoft describes WACK as deprecated and no longer maintained, but still available for optional local checks; Partner Center performs the required certification after submission. See [local package validation](https://learn.microsoft.com/en-us/windows/msix/package/packaging-uwp-apps#validate-your-app-package) and [MSIX submission certification](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/create-app-submission?pivots=store-installer-msix).
- [ ] On a clean test account or VM, install only a properly signed local test package when needed; verify app launch, image opening from Explorer, file activation with paths containing spaces, and user-selected file associations. Do not install a test certificate on a daily-use machine as part of this preparation.
- [ ] Test only the formats the client actually supports: PNG, JPEG, GIF, WebP, AVIF, BMP, TIFF, and ICO. HEIC/HEIF are not claimed or registered as supported formats.
- [ ] Confirm upgrade behavior from the previous package and verify uninstall removes the app without deleting user images.
- [ ] Review the actual package and listing together before submission; do not treat successful MakeAppx packing or a WACK pass as Store approval.

## Current release blockers

The Partner Center product reservation and exact package identity/Publisher, a verified publisher account, published support/privacy/EULA URLs, localized screenshots and Store logo assets, final app properties and age rating, and the actual MSIX build/package validation are still outstanding. No listing has been submitted and no certification result exists.

## Final submission

- [ ] Upload the package through Partner Center and respond to any package validation findings.
- [ ] Review all Store metadata and privacy answers in the submission form.
- [ ] Submit for certification only after the final identity, version, package, and listing agree.
- [ ] Confirm the public Store listing and installation after Microsoft publishes the submission.
