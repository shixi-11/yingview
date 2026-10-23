# Microsoft Store purchases

The preview has no configured product and cannot take payments. Everyday viewing, basic appearance editing, theme import/export, and the comparison preview are free. The personal style library is the implemented Pro feature; additional professional workflows have not been promised for sale.

Before enabling checkout:

1. Reserve the app and obtain its exact package identity and Publisher from Partner Center. Package with `scripts/package-store.ps1` and test a Store-associated build.
2. Create a permanent **Durable** Pro add-on and copy its product Store ID into `src/YingView/Commerce/store-config.json`. This is the product ID, not an offer token or a SKU ID. Rebuild the app; the configuration is embedded.
3. Approve the buyer's commercial-use terms, price, territories, support and update policy before publishing the add-on. The source license remains PolyForm Noncommercial; this preview does not silently grant an additional commercial license.
4. Test purchase cancellation, failed/pending checkout, successful purchase, restore, expired/revoked entitlement, and offline restart using the real Store app and add-on. Unit tests do not validate the Store service.

`GetAppLicenseAsync` is queried before prices. Active add-on SKU licenses are matched at the configured product ID boundary, so an unrelated add-on cannot unlock Pro. A purchase response alone never grants Pro; an active Store license is required. A missing catalog, missing price, unverified license, or pending transaction disables purchase. The app does not cache an editable Pro flag in preferences.

The Microsoft Store owns checkout and payment details. The app displays the localized Store price rather than a hard-coded price. No purchase, checkout test, or billing integration has been completed against a real product yet.

References: [durable add-on licenses](https://learn.microsoft.com/en-us/uwp/api/windows.services.store.storeapplicense.addonlicenses), [cached app license](https://learn.microsoft.com/en-us/uwp/api/windows.services.store.storecontext.getapplicenseasync), [purchase API](https://learn.microsoft.com/en-us/uwp/api/windows.services.store.storecontext.requestpurchaseasync).
