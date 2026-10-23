using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace YingView.Commerce;

/// <summary>Displays only features and prices that are actually available.</summary>
public sealed class PlansWindow : Window
{
    private readonly StoreBillingService _billing;
    private readonly bool _zh;
    private readonly TextBlock _status;
    private readonly Button _buy;
    private readonly Button _restore;
    private bool _busy;
    private string T(string zh, string en) => _zh ? zh : en;

    public PlansWindow(StoreBillingService billing, bool isZh)
    {
        _billing = billing; _zh = isZh;
        Title = T("个人风格与专业版", "Personal styles & Pro");
        Width = 540; Height = 480; MinWidth = 430; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ViewerTheme.Brush("#F4F5F3"); Foreground = ViewerTheme.Brush("#24282D");
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        Resources["HoverBrush"] = ViewerTheme.Brush("#DDE5E2");
        Resources["PressedBrush"] = ViewerTheme.Brush("#CAD9D3");
        var style = new Style(typeof(Button), (Style)Application.Current.FindResource(typeof(Button)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Foreground));
        style.Setters.Add(new Setter(Control.BackgroundProperty, ViewerTheme.Brush("#E6EBE8")));
        Resources[typeof(Button)] = style;
        var root = new DockPanel { Margin = new Thickness(28) }; Content = root;
        var footer = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = ViewerTheme.Brush("#59635E"), Margin = new Thickness(0, 0, 0, 12) };
        footer.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _buy = new Button { Padding = new Thickness(18, 9, 18, 9), Background = ViewerTheme.Brush("#28766C"), Foreground = Brushes.White };
        _restore = new Button { Content = T("恢复购买", "Restore purchase"), Margin = new Thickness(10, 0, 0, 0) };
        actions.Children.Add(_buy); actions.Children.Add(_restore); footer.Children.Add(actions);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text = T("让看图更合心意", "Make it feel like yours"), FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 20) });
        body.Children.Add(new TextBlock { Text = T("基础功能 · 免费", "Everyday viewing · Free"), FontSize = 16, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = T("完整显示、翻图、删除到回收站、透明背景、对照审阅，以及颜色与文字大小调整。", "Fit-to-view browsing, Recycle Bin deletion, transparency backgrounds, comparison, and color and text-size adjustments."), TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 8, 0, 22) });
        body.Children.Add(new TextBlock { Text = T("专业版 · 个人风格库", "Pro · Personal style library"), FontSize = 16, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = T("保存多套具名风格，随时切换、管理。一次购买，无订阅。", "Save, switch, and manage your own named styles. A one-time purchase, with no subscription."), TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 8, 0, 12) });
        _buy.Click += async (_, _) =>
        {
            if (_busy || !_billing.Snapshot.CanPurchase || string.IsNullOrWhiteSpace(_billing.Snapshot.PriceText)) return;
            await Run(() => _billing.BuyProAsync());
        };
        _restore.Click += async (_, _) => await Run(() => _billing.RestoreAsync());
        _billing.SnapshotChanged += Changed;
        Closed += (_, _) => _billing.SnapshotChanged -= Changed;
        Loaded += async (_, _) => await Run(() => _billing.LoadAsync());
        Refresh();
    }

    private void Changed(object? sender, BillingSnapshot snapshot) => Refresh();
    private async Task Run(Func<Task<BillingSnapshot>> operation)
    {
        if (_busy) return;
        _busy = true; Refresh();
        try { await operation(); }
        finally { _busy = false; Refresh(); }
    }
    private void Refresh()
    {
        var value = _billing.Snapshot;
        _buy.Content = value.HasPro ? T("专业版已启用", "Pro is active") : string.IsNullOrWhiteSpace(value.PriceText) ? T("购买暂未开放", "Purchases coming later") : T("一次性购买 · ", "Buy once · ") + value.PriceText;
        _buy.IsEnabled = !_busy && !value.HasPro && value.CanPurchase && !string.IsNullOrWhiteSpace(value.PriceText);
        _restore.IsEnabled = !_busy && value.Status != BillingStatus.NotConfigured;
        _status.Text = _busy ? T("正在查询 Microsoft Store…", "Checking Microsoft Store…") : value.Status == BillingStatus.NotConfigured
            ? T("预览版暂未开放购买，基础功能可继续免费使用。", "Purchases are not available in this preview. Everyday features remain free to use.") : value.Message;
    }
}
