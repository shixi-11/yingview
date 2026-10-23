using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using Windows.Services.Store;
using WinRT.Interop;

namespace YingView.Commerce;

/// <summary>Microsoft Store billing for the configured permanent Pro add-on.</summary>
public sealed class StoreBillingService
{
    private readonly IntPtr _ownerHwnd;
    private readonly bool _isZh;
    private readonly StoreConfiguration _configuration;
    private StoreContext? _context;
    private string _priceText = "";
    private bool _catalogValid;
    private bool _purchaseConfirmationPending;
    private bool? _verifiedHasPro;

    public StoreBillingService(IntPtr hwnd, bool isZh)
    {
        _ownerHwnd = hwnd;
        _isZh = isZh;
        _configuration = LoadConfiguration();
    }

    public BillingSnapshot Snapshot { get; private set; } = new(false, false, "", BillingStatus.NotConfigured, "");
    public event EventHandler<BillingSnapshot>? SnapshotChanged;

    public async Task<BillingSnapshot> LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(_configuration.ProProductStoreId))
        {
            InvalidateCatalog();
            return Publish(BillingStatus.NotConfigured,
                Text("Microsoft Store 商品尚未配置。", "Microsoft Store product is not configured."));
        }
        if (!HasPackageIdentity())
        {
            InvalidateCatalog();
            return Publish(BillingStatus.NotConfigured,
                Text("请从 Microsoft Store 安装本应用后使用购买功能。", "Install this app from Microsoft Store to enable purchases."));
        }

        try
        {
            EnsureContext();
            await RefreshEntitlementAsync();
        }
        catch
        {
            InvalidateCatalog();
            return Publish(BillingStatus.StoreUnavailable, StoreUnavailableMessage());
        }

        // License lookup comes first. GetAppLicenseAsync returns the Store's cached license while offline.
        // An active Pro license remains usable even when product listing or price lookup is unavailable.
        if (_verifiedHasPro == true)
        {
            InvalidateCatalog();
            return Publish(BillingStatus.ProActive, ProActiveMessage());
        }
        if (_purchaseConfirmationPending)
        {
            InvalidateCatalog();
            return Publish(BillingStatus.PurchasePending, PurchasePendingMessage());
        }

        try
        {
            if (!await RefreshCatalogAsync())
                return Publish(BillingStatus.NotConfigured, ProductUnavailableMessage());
            return Publish(BillingStatus.Ready, ReadyMessage());
        }
        catch
        {
            InvalidateCatalog();
            return Publish(BillingStatus.StoreUnavailable, StoreUnavailableMessage());
        }
    }

    public async Task<BillingSnapshot> BuyProAsync()
    {
        if (string.IsNullOrWhiteSpace(_configuration.ProProductStoreId))
            return LoadNotConfigured();
        if (!HasPackageIdentity())
        {
            InvalidateCatalog();
            return Publish(BillingStatus.NotConfigured,
                Text("请从 Microsoft Store 安装本应用后使用购买功能。", "Install this app from Microsoft Store to enable purchases."));
        }

        try
        {
            EnsureContext();
            await RefreshEntitlementAsync();
            if (_verifiedHasPro == true)
            {
                InvalidateCatalog();
                return Publish(BillingStatus.ProActive, ProActiveMessage());
            }
            if (_purchaseConfirmationPending)
                return Publish(BillingStatus.PurchasePending, PurchasePendingMessage());

            // Revalidate both the durable license and the exact configured catalog item at click time.
            if (!await RefreshCatalogAsync())
                return Publish(BillingStatus.NotConfigured, ProductUnavailableMessage());

            if (!StoreBillingDecisions.CanPurchase(
                    _verifiedHasPro,
                    _catalogValid,
                    _priceText,
                    _purchaseConfirmationPending))
                return Publish(BillingStatus.NotConfigured, ProductUnavailableMessage());
        }
        catch
        {
            InvalidateCatalog();
            return Publish(BillingStatus.StoreUnavailable, StoreUnavailableMessage());
        }

        // RequestPurchaseAsync displays Store UI and must run on the UI thread.
        try
        {
            var result = await _context!.RequestPurchaseAsync(_configuration.ProProductStoreId);
            if (result.Status == StorePurchaseStatus.NotPurchased)
            {
                _purchaseConfirmationPending = false;
                return Publish(BillingStatus.PurchaseCanceled, PurchaseNotCompletedMessage());
            }

            if (result.Status == StorePurchaseStatus.Succeeded || result.Status == StorePurchaseStatus.AlreadyPurchased)
            {
                _purchaseConfirmationPending = true;
                await RefreshEntitlementAsync();
                if (_verifiedHasPro == true)
                {
                    _purchaseConfirmationPending = false;
                    return Publish(BillingStatus.ProActive, ProActiveMessage());
                }
                return Publish(BillingStatus.PurchasePending, PurchasePendingMessage());
            }

            // Store reports that checkout did not succeed. Retry is allowed only if the license
            // lookup also completes and still confirms that the configured add-on is not owned.
            _purchaseConfirmationPending = true;
            try
            {
                await RefreshEntitlementAsync();
                _purchaseConfirmationPending = StoreBillingDecisions.KeepPendingAfterFailedPurchase(_verifiedHasPro);
            }
            catch { }
            return Publish(_verifiedHasPro == true ? BillingStatus.ProActive : BillingStatus.PurchaseFailed,
                _verifiedHasPro == true ? ProActiveMessage() : PurchaseFailedMessage());
        }
        catch
        {
            // The request may have reached the Store before an error was reported.
            // Preserve the last Store-verified entitlement; an exception cannot grant Pro.
            _purchaseConfirmationPending = true;
            try { await RefreshEntitlementAsync(); }
            catch { }
            return Publish(_verifiedHasPro == true ? BillingStatus.ProActive : BillingStatus.PurchaseFailed,
                _verifiedHasPro == true ? ProActiveMessage() : PurchaseFailedMessage());
        }
    }

    public async Task<BillingSnapshot> RestoreAsync()
    {
        if (string.IsNullOrWhiteSpace(_configuration.ProProductStoreId))
            return LoadNotConfigured();
        if (!HasPackageIdentity())
        {
            InvalidateCatalog();
            return Publish(BillingStatus.NotConfigured,
                Text("请从 Microsoft Store 安装本应用后查询授权。", "Install this app from Microsoft Store to check for purchases."));
        }

        try
        {
            EnsureContext();
            // Restore the durable entitlement before attempting any listing or price request.
            await RefreshEntitlementAsync();
            if (_verifiedHasPro == true)
            {
                _purchaseConfirmationPending = false;
                InvalidateCatalog();
                return Publish(BillingStatus.ProActive, ProActiveMessage());
            }

            if (_purchaseConfirmationPending)
                return Publish(BillingStatus.PurchasePending, PurchasePendingMessage());

            if (!await RefreshCatalogAsync())
                return Publish(BillingStatus.NotConfigured, ProductUnavailableMessage());
            return Publish(BillingStatus.RestoreComplete, RestoreNotFoundMessage());
        }
        catch
        {
            InvalidateCatalog();
            return Publish(BillingStatus.StoreUnavailable, RestoreUnavailableMessage());
        }
    }

    private async Task<bool> RefreshCatalogAsync()
    {
        InvalidateCatalog();
        var result = await _context!.GetStoreProductsAsync(
            new[] { "Durable" }, new[] { _configuration.ProProductStoreId });
        if (result.ExtendedError is not null) throw result.ExtendedError;
        var product = result.Products.Values.FirstOrDefault(p =>
            string.Equals(p.StoreId, _configuration.ProProductStoreId, StringComparison.OrdinalIgnoreCase));
        var formattedPrice = product?.Price?.FormattedPrice;
        if (string.IsNullOrWhiteSpace(formattedPrice)) return false;

        _priceText = formattedPrice;
        _catalogValid = true;
        return true;
    }

    private async Task RefreshEntitlementAsync()
    {
        var license = await _context!.GetAppLicenseAsync();
        if (license is null) throw new InvalidOperationException();

        var hasPro = EntitlementRules.HasConfiguredProLicense(
            license.AddOnLicenses.Select(pair => new KeyValuePair<string, bool>(pair.Key, pair.Value.IsActive)),
            _configuration.ProProductStoreId);
        _verifiedHasPro = StoreBillingDecisions.PreserveVerifiedEntitlement(_verifiedHasPro, hasPro);
    }

    private BillingSnapshot LoadNotConfigured()
    {
        InvalidateCatalog();
        return Publish(BillingStatus.NotConfigured,
            Text("Microsoft Store 商品尚未配置。", "Microsoft Store product is not configured."));
    }

    private void InvalidateCatalog()
    {
        _catalogValid = false;
        _priceText = "";
    }

    private void EnsureContext()
    {
        if (_context is not null) return;
        _context = StoreContext.GetDefault();
        InitializeWithWindow.Initialize(_context, _ownerHwnd);
    }

    private BillingSnapshot Publish(BillingStatus status, string message)
    {
        Snapshot = StoreBillingDecisions.CreateSnapshot(
            _verifiedHasPro,
            _catalogValid,
            _priceText,
            _purchaseConfirmationPending,
            status,
            message);
        SnapshotChanged?.Invoke(this, Snapshot);
        return Snapshot;
    }

    private string Text(string zh, string en) => _isZh ? zh : en;
    private string StoreUnavailableMessage() => _verifiedHasPro == true
        ? Text("专业版已启用；暂时无法更新 Microsoft Store 信息。", "Pro remains active; Microsoft Store information could not be refreshed.")
        : Text("暂时无法连接 Microsoft Store，请检查网络后重试。", "Microsoft Store is unavailable. Check your connection and try again.");
    private string RestoreUnavailableMessage() => Text("暂时无法查询购买记录，请稍后重试。", "Purchases could not be checked. Please try again later.");
    private string ProductUnavailableMessage() => Text("暂时无法确认专业版商品及价格，购买功能当前不可用。", "The Pro product and price could not be confirmed. Purchasing is unavailable.");
    private string ReadyMessage() => Text("可通过 Microsoft Store 购买。", "Ready to purchase through Microsoft Store.");
    private string ProActiveMessage() => Text("专业版已启用。", "Pro is active.");
    private string PurchasePendingMessage() => Text("Microsoft Store 正在确认购买授权，请稍后重新查询。", "Microsoft Store is confirming the purchase. Check again shortly.");
    private string PurchaseNotCompletedMessage() => Text("购买未完成，可能已取消。", "The purchase did not complete. It may have been canceled.");
    private string PurchaseFailedMessage() => Text("购买未完成，请检查订单记录后再试。", "Purchase did not complete. Check your order history before trying again.");
    private string RestoreNotFoundMessage() => Text("未找到此 Microsoft Store 账号的专业版授权。", "No Pro license was found for this Microsoft Store account.");

    private static bool HasPackageIdentity()
    {
        var length = 0;
        return GetCurrentPackageFullName(ref length, null) == 122;
    }

    private static StoreConfiguration LoadConfiguration()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("store-config.json", StringComparison.OrdinalIgnoreCase));
        if (resource is not null)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is not null)
                return JsonSerializer.Deserialize<StoreConfiguration>(stream) ?? new StoreConfiguration();
        }
        var path = Path.Combine(AppContext.BaseDirectory, "store-config.json");
        if (File.Exists(path))
            return JsonSerializer.Deserialize<StoreConfiguration>(File.ReadAllText(path)) ?? new StoreConfiguration();
        return new StoreConfiguration();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, [Out] char[]? packageFullName);

    private sealed class StoreConfiguration
    {
        public string ProProductStoreId { get; set; } = "";
    }
}

internal static class StoreBillingDecisions
{
    internal static bool CanPurchase(bool? verifiedHasPro, bool catalogValid, string priceText, bool purchaseConfirmationPending) =>
        verifiedHasPro == false && catalogValid && !string.IsNullOrWhiteSpace(priceText) && !purchaseConfirmationPending;

    internal static BillingSnapshot CreateSnapshot(
        bool? verifiedHasPro,
        bool catalogValid,
        string priceText,
        bool purchaseConfirmationPending,
        BillingStatus status,
        string message)
    {
        var hasPro = verifiedHasPro == true;
        var price = catalogValid ? priceText : "";
        var canPurchase = CanPurchase(verifiedHasPro, catalogValid, price, purchaseConfirmationPending);
        var effectiveStatus = hasPro ? BillingStatus.ProActive : status;
        return new BillingSnapshot(hasPro, canPurchase, price, effectiveStatus, message);
    }

    internal static bool? PreserveVerifiedEntitlement(bool? previouslyVerified, bool? newlyVerified) =>
        newlyVerified ?? previouslyVerified;

    internal static bool KeepPendingAfterFailedPurchase(bool? verifiedHasPro) => verifiedHasPro != false;
}
