using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace YingView.Files;

/// <summary>Reports actual associations; Windows owns the final default-app choice.</summary>
public sealed class DefaultAppsWindow : Window
{
    private readonly string _language;
    private readonly bool _zh;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly StackPanel _formats = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 16) };
    private string T(string zh, string en) => _zh ? zh : en;

    public DefaultAppsWindow(string language)
    {
        _language = language; _zh = language == "zh";
        Title = T("默认看图软件", "Default image viewer");
        Width = 520; Height = 540; MinWidth = 460; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BarBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(FontSizeProperty, "ControlTextSize");
        var root = new DockPanel { Margin = new Thickness(24) };
        TextElement.SetForeground(root, Foreground);
        TextElement.SetFontSize(root, FontSize);
        Content = new Border { Background = Background, Child = root };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var refresh = new Button { Content = T("重新检查", "Check again"), Margin = new Thickness(0, 0, 8, 0) };
        refresh.Click += (_, _) => Refresh(); actions.Children.Add(refresh);
        var open = new Button { Content = T("前往系统设置", "Open Windows Settings") };
        open.SetResourceReference(BackgroundProperty, "HoverBrush");
        open.Click += (_, _) =>
        {
            var result = DefaultAppService.RegisterAndOpen(_language);
            _message.Text = result.SettingsLaunchSucceeded
                ? T("请在 Windows 页面选择映窗并确认。返回后可重新检查。", "Choose YingView in Windows and confirm, then return here to check.")
                : T("系统设置暂时无法打开。可在 Windows 设置的“应用 → 默认应用”中选择映窗。", "Windows Settings could not open. Go to Apps → Default apps and choose YingView.");
        };
        actions.Children.Add(open); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = T("用映窗打开图片", "Open images with YingView"), FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        body.Children.Add(_status); body.Children.Add(_formats); body.Children.Add(_message);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Activated += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var associations = DefaultAppService.GetAssociations(_language);
        var count = associations.Count(a => a.IsYingView);
        _status.Text = T($"已设为 {count}/{associations.Count} 种图片格式的默认应用。", $"Default for {count} of {associations.Count} image formats.");
        _formats.Children.Clear();
        foreach (var association in associations)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(new TextBlock { Text = association.Extension, Width = 76 });
            row.Children.Add(new TextBlock { Text = association.IsYingView ? T("已设置", "Set") : T("尚未设置", "Not set") });
            _formats.Children.Add(row);
        }
        if (count == associations.Count) _message.Text = T("这些图片格式现在会默认用映窗打开。", "These image formats now open with YingView by default.");
        else _message.Text = T("前往系统设置，选择映窗并确认需要关联的图片格式。Windows 会保留你的最终选择。", "Open Windows Settings, choose YingView, and confirm the image formats you want to associate.");
    }
}
