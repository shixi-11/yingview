using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace YingView.Files;

/// <summary>Starts the supported Windows flow and checks the actual result on return.</summary>
public sealed class DefaultAppsWindow : Window
{
    private readonly string _language;
    private readonly bool _zh;
    private readonly Func<string, DefaultAppService.OpenResult> _openSettings;
    private readonly Func<string, IReadOnlyList<DefaultAppService.Association>> _readAssociations;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
    private readonly StackPanel _formats = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) };
    private readonly Button _openButton;
    private bool _started, _opening, _waiting, _launchFailed, _allSet;
    private string T(string zh, string en) => _zh ? zh : en;

    public DefaultAppsWindow(string language, bool startImmediately = false)
        : this(language, startImmediately, DefaultAppService.RegisterAndOpen, DefaultAppService.GetAssociations) { }

    internal DefaultAppsWindow(string language, bool startImmediately,
        Func<string, DefaultAppService.OpenResult> openSettings,
        Func<string, IReadOnlyList<DefaultAppService.Association>> readAssociations)
    {
        _language = language; _zh = language == "zh";
        _openSettings = openSettings; _readAssociations = readAssociations;
        Title = T("默认看图软件", "Default image viewer");
        Width = 520; Height = 330; MinWidth = 460; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BarBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(FontSizeProperty, "ControlTextSize");
        var root = new DockPanel { Margin = new Thickness(24) };
        TextElement.SetForeground(root, Foreground);
        TextElement.SetFontSize(root, FontSize);
        Content = new Border { Background = Background, Child = root };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var close = new Button { Content = T("关闭", "Close"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        close.Click += (_, _) => Close(); actions.Children.Add(close);
        _openButton = new Button { Content = T("设为默认", "Set as default"), Padding = new Thickness(18, 8, 18, 8) };
        _openButton.SetResourceReference(BackgroundProperty, "AccentBrush");
        _openButton.SetResourceReference(ForegroundProperty, "AccentTextBrush");
        _openButton.Click += (_, _) => StartSetup(); actions.Children.Add(_openButton);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = T("用映窗打开图片", "Open images with YingView"), FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        body.Children.Add(_status); body.Children.Add(_message);
        var details = new Expander { Header = T("查看图片格式", "Image formats"), Content = _formats, Foreground = Foreground };
        details.Expanded += (_, _) => Height = Math.Min(620, SystemParameters.WorkArea.Height - 48);
        details.Collapsed += (_, _) => Height = 330;
        body.Children.Add(details);
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Activated += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            if (_started) return;
            _started = true;
            if (startImmediately && !_allSet) StartSetup();
        };
        Refresh();
    }

    private void StartSetup()
    {
        if (_opening || _allSet) return;
        _opening = true; _openButton.IsEnabled = false;
        try
        {
            var result = _openSettings(_language);
            _waiting = result.SettingsLaunchSucceeded;
            _launchFailed = !result.SettingsLaunchSucceeded;
        }
        catch { _waiting = false; _launchFailed = true; }
        finally { _opening = false; Refresh(); }
    }

    private void Refresh()
    {
        var associations = _readAssociations(_language);
        var count = associations.Count(a => a.IsYingView);
        _allSet = associations.Count > 0 && count == associations.Count;
        _status.Text = _allSet ? T("映窗已是默认看图软件", "YingView is your default image viewer")
            : count == 0 ? T("尚未设为默认看图软件", "Not your default image viewer yet")
            : T($"已关联 {count}/{associations.Count} 种图片格式", $"Default for {count} of {associations.Count} image formats");
        _formats.Children.Clear();
        foreach (var association in associations)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(new TextBlock { Text = association.Extension, Width = 76 });
            row.Children.Add(new TextBlock { Text = association.IsYingView ? T("已设置", "Set") : T("尚未设置", "Not set") });
            _formats.Children.Add(row);
        }
        _openButton.IsEnabled = !_opening && !_allSet;
        _openButton.Content = _allSet ? T("已设为默认", "Set as default") : T("设为默认", "Set as default");
        _message.Text = _allSet ? T("支持的图片格式现在会默认用映窗打开。", "Supported image formats now open with YingView by default.")
            : _launchFailed ? T("未能打开系统设置，请重试。也可在 Windows“应用 → 默认应用”中选择映窗。", "Windows Settings could not open. Try again, or choose YingView under Apps → Default apps.")
            : _waiting ? T("请在 Windows 中选择映窗并确认。返回这里时，结果会自动更新。", "Choose YingView in Windows and confirm. The result updates automatically when you return.")
            : T("点击“设为默认”开始。Windows 会要求你确认，结果会自动更新。", "Select Set as default to begin. Windows will ask you to confirm, and the result updates automatically.");
    }
}
