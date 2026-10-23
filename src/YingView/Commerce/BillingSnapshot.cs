namespace YingView.Commerce;

public enum BillingStatus
{
    NotConfigured,
    Ready,
    ProActive,
    PurchaseCanceled,
    PurchasePending,
    PurchaseFailed,
    RestoreComplete,
    StoreUnavailable
}

public sealed record BillingSnapshot(
    bool HasPro,
    bool CanPurchase,
    string PriceText,
    BillingStatus Status,
    string Message);

internal static class EntitlementRules
{
    internal static bool HasConfiguredProLicense(
        IEnumerable<KeyValuePair<string, bool>> addonLicenses,
        string configuredStoreId)
    {
        if (string.IsNullOrWhiteSpace(configuredStoreId)) return false;
        // AddOnLicenses uses SKU Store IDs (product/sku), whereas checkout uses the product ID.
        // Match the product boundary, never an arbitrary prefix shared by another add-on.
        return addonLicenses.Any(item => item.Value &&
            (string.Equals(item.Key, configuredStoreId, StringComparison.OrdinalIgnoreCase) ||
             item.Key.StartsWith(configuredStoreId + "/", StringComparison.OrdinalIgnoreCase) &&
             item.Key.Length > configuredStoreId.Length + 1 &&
             !item.Key[(configuredStoreId.Length + 1)..].Contains('/')));
    }

    internal static BillingStatus MapPurchaseResult(bool succeeded, bool canceled, bool alreadyPurchased)
    {
        if (succeeded || alreadyPurchased) return BillingStatus.PurchasePending;
        return canceled ? BillingStatus.PurchaseCanceled : BillingStatus.PurchaseFailed;
    }
}
