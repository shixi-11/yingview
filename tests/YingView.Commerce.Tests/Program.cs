using YingView.Commerce;

var checks = new (string Name, bool Passed)[]
{
    ("configured active add-on grants Pro", HasLicense("pro-store-id", "pro-store-id", true)),
    ("wrong add-on never grants Pro", !HasLicense("another-addon", "pro-store-id", true)),
    ("expired add-on never grants Pro", !HasLicense("pro-store-id", "pro-store-id", false)),
    ("unconfigured product never grants Pro", !HasLicense("pro-store-id", "", true)),
    ("active Store SKU grants configured product", HasLicense("9PRO12345678/0010", "9PRO12345678", true)),
    ("another product sharing a prefix never grants Pro", !HasLicense("9PRO12345678OTHER/0010", "9PRO12345678", true)),
    ("inactive Store SKU never grants Pro", !HasLicense("9PRO12345678/0010", "9PRO12345678", false)),
    ("empty SKU never grants Pro", !HasLicense("9PRO12345678/", "9PRO12345678", true)),
    ("canceled purchase stays canceled", EntitlementRules.MapPurchaseResult(false, true, false) == BillingStatus.PurchaseCanceled),
    ("failed purchase stays failed", EntitlementRules.MapPurchaseResult(false, false, false) == BillingStatus.PurchaseFailed),
    ("success requires a license refresh", EntitlementRules.MapPurchaseResult(true, false, false) == BillingStatus.PurchasePending),
    ("already purchased requires a license refresh", EntitlementRules.MapPurchaseResult(false, false, true) == BillingStatus.PurchasePending),
    ("buy requires verified unpurchased license", StoreBillingDecisions.CanPurchase(false, true, "¥39", false)),
    ("unknown license cannot buy", !StoreBillingDecisions.CanPurchase(null, true, "¥39", false)),
    ("owned Pro cannot buy again", !StoreBillingDecisions.CanPurchase(true, true, "¥39", false)),
    ("missing catalog cannot buy", !StoreBillingDecisions.CanPurchase(false, false, "¥39", false)),
    ("missing price cannot buy", !StoreBillingDecisions.CanPurchase(false, true, "", false)),
    ("pending purchase cannot be repeated", !StoreBillingDecisions.CanPurchase(false, true, "¥39", true)),
    ("catalog failure preserves verified Pro", SnapshotAfterFailure(true).HasPro),
    ("catalog failure does not grant Pro", !SnapshotAfterFailure(false).HasPro),
    ("catalog failure never enables buying", !SnapshotAfterFailure(false).CanPurchase),
    ("failed purchase preserves previously verified Pro", SnapshotForPurchaseResult(true, BillingStatus.PurchaseFailed).HasPro),
    ("canceled purchase preserves previously verified Pro", SnapshotForPurchaseResult(true, BillingStatus.PurchaseCanceled).HasPro),
    ("failed purchase does not grant Pro", !SnapshotForPurchaseResult(false, BillingStatus.PurchaseFailed).HasPro),
    ("license lookup failure retains prior verified state", StoreBillingDecisions.PreserveVerifiedEntitlement(true, null) == true),
    ("license lookup failure does not invent a license", StoreBillingDecisions.PreserveVerifiedEntitlement(false, null) == false),
    ("license lookup failure does not grant an unknown license", StoreBillingDecisions.PreserveVerifiedEntitlement(null, null) is null),
    ("failed purchase can retry after verified no-license result", !StoreBillingDecisions.KeepPendingAfterFailedPurchase(false)),
    ("failed purchase stays pending without a verified license result", StoreBillingDecisions.KeepPendingAfterFailedPurchase(null)),
    ("failed purchase stays pending when an existing license remains verified", StoreBillingDecisions.KeepPendingAfterFailedPurchase(true))
};

foreach (var check in checks) Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
return checks.All(check => check.Passed) ? 0 : 1;

static bool HasLicense(string key, string configured, bool active) =>
    EntitlementRules.HasConfiguredProLicense(new[] { new KeyValuePair<string, bool>(key, active) }, configured);

static BillingSnapshot SnapshotAfterFailure(bool previouslyVerifiedPro) =>
    StoreBillingDecisions.CreateSnapshot(previouslyVerifiedPro, false, "", false,
        BillingStatus.StoreUnavailable, "Store unavailable");

static BillingSnapshot SnapshotForPurchaseResult(bool previouslyVerifiedPro, BillingStatus status) =>
    StoreBillingDecisions.CreateSnapshot(previouslyVerifiedPro, true, "¥39", false, status, "Purchase did not complete");
