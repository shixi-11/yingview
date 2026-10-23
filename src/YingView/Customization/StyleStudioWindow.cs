using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace YingView;

/// <summary>A small, live-preview editor for the viewer's appearance.</summary>
public sealed class StyleStudioWindow : Window
{
    private readonly Preferences _settings;
    private readonly Action<ViewerTheme> _previewTheme;
    private readonly Action<double> _previewTextScale;
    private readonly Action<bool> _previewCompact;
    private readonly bool _hasPro;
    private readonly ViewerTheme _originalTheme;
    private readonly double _originalScale;
    private readonly bool _originalCompact;
    private readonly List<ViewerTheme> _workingStyles;
    private ViewerTheme _working;
    private ComboBox _colorPart = null!;
    private TextBox _name = null!, _hex = null!;
    private Slider _scale = null!;
    private CheckBox _compact = null!;
    private TextBlock _sample = null!;
    private StackPanel _swatches = null!;
    private bool _updating;

    public bool Applied { get; private set; }
    public bool ProRequested { get; private set; }

    public StyleStudioWindow(Preferences settings, Action<ViewerTheme> previewTheme, Action<double> previewTextScale, Action<bool> previewCompact, bool hasPro)
    {
        _settings = settings; _previewTheme = previewTheme; _previewTextScale = previewTextScale; _previewCompact = previewCompact; _hasPro = hasPro;
        _originalTheme = settings.Theme; _working = settings.Theme; _originalScale = settings.TextScale; _originalCompact = settings.CompactToolbar;
        _workingStyles = settings.PersonalStyles.ToList();
        var zh = settings.Language == "zh";
        Title = zh ? "界面风格" : "Appearance";
        Width = Math.Min(680, SystemParameters.WorkArea.Width); MinWidth = Math.Min(620, SystemParameters.WorkArea.Width); MaxWidth = Math.Min(760, SystemParameters.WorkArea.Width); SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brush("#F2F3F4"); Foreground = Brush("#20242A");
        Resources["HoverBrush"] = Brush("#DDE5E2"); Resources["PressedBrush"] = Brush("#CAD9D3");
        var root = new DockPanel { Margin = new Thickness(22), Background = Background }; TextElement.SetForeground(root, Foreground); Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        body.Children.Add(new TextBlock { Text = zh ? "选择一套风格，再按喜好微调。变化会即时预览。" : "Choose a style, then tune it to your liking. Changes preview live.", FontSize = 14, Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap });
        var presets = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
        foreach (var preset in ViewerTheme.BuiltIn) presets.Children.Add(PresetButton(preset));
        body.Children.Add(presets);
        body.Children.Add(new TextBlock { Text = zh ? "当前风格名称" : "Current style name", FontWeight = FontWeights.SemiBold });
        _name = new TextBox { Text = ViewerTheme.LocalizedName(_working.Name, zh), Margin = new Thickness(0, 5, 0, 12), Padding = new Thickness(8), MaxLength = 60 };
        _name.TextChanged += (_, _) => { if (!_updating) _working = _working with { Name = _name.Text.Trim() }; }; body.Children.Add(_name);
        body.Children.Add(new TextBlock { Text = zh ? "调整颜色" : "Adjust colors", FontWeight = FontWeights.SemiBold });
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 8) };
        _colorPart = new ComboBox { Width = 145, SelectedIndex = 0, Margin = new Thickness(0, 0, 10, 0) };
        foreach (var part in zh ? new[] { "窗口背景", "工具栏", "正文文字", "辅助文字", "强调色", "画布背景" } : new[] { "Window", "Toolbar", "Text", "Muted text", "Accent", "Canvas" }) _colorPart.Items.Add(part);
        _colorPart.SelectionChanged += (_, _) => RefreshSwatches(); DockPanel.SetDock(_colorPart, Dock.Left); row.Children.Add(_colorPart);
        _swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_swatches); body.Children.Add(row);
        var advanced = new Expander { Header = zh ? "高级颜色设置" : "Advanced color", IsExpanded = false, Margin = new Thickness(0, 0, 0, 12) };
        var advancedRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        _hex = new TextBox { Width = 100, Padding = new Thickness(5), ToolTip = "#RRGGBB" };
        var setHex = new Button { Content = zh ? "应用颜色" : "Set color", Margin = new Thickness(8, 0, 0, 0), Style = StudioButtonStyle() };
        setHex.Click += (_, _) => ApplyHex(zh);
        advancedRow.Children.Add(_hex); advancedRow.Children.Add(setHex); advanced.Content = advancedRow; body.Children.Add(advanced);
        body.Children.Add(new TextBlock { Text = zh ? "界面文字大小" : "Interface text size", FontWeight = FontWeights.SemiBold });
        _scale = new Slider { Minimum = 0.85, Maximum = 1.3, Value = Math.Clamp(settings.TextScale, .85, 1.3), TickFrequency = .05, IsSnapToTickEnabled = false, Margin = new Thickness(0, 2, 0, 8) };
        AutomationProperties.SetName(_scale, zh ? "界面文字大小" : "Interface text size"); _scale.ValueChanged += (_, _) => { _sample.FontSize = 15 * _scale.Value; _previewTextScale(_scale.Value); }; body.Children.Add(_scale);
        _compact = new CheckBox { Content = zh ? "紧凑工具栏" : "Compact toolbar", IsChecked = settings.CompactToolbar, Margin = new Thickness(0, 0, 0, 12) };
        _compact.Checked += (_, _) => _previewCompact(true); _compact.Unchecked += (_, _) => _previewCompact(false); body.Children.Add(_compact);
        _sample = new TextBlock { Text = zh ? "预览文字 · 映窗" : "Preview text · YingView", FontSize = 15 * _scale.Value, Padding = new Thickness(14), TextWrapping = TextWrapping.Wrap };
        var sampleBorder = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(0), Child = _sample, Margin = new Thickness(0, 0, 0, 5) };
        body.Children.Add(sampleBorder);
        var reset = new Button { Content = zh ? "恢复默认" : "Reset", Margin = new Thickness(0, 0, 10, 0), Style = StudioButtonStyle() }; reset.Click += (_, _) => Reset(); footer.Children.Add(reset);
        var library = new Button { Content = zh ? (_hasPro ? "个人风格" : "个人风格 · 专业版") : (_hasPro ? "My styles" : "My styles · Pro"), Margin = new Thickness(0, 0, 10, 0), Style = StudioButtonStyle() };
        library.Click += (_, _) => { ProRequested = !_hasPro; if (_hasPro) ShowLibrary(zh); else Close(); }; footer.Children.Add(library);
        var cancel = new Button { Content = zh ? "取消" : "Cancel", Margin = new Thickness(0, 0, 8, 0), Style = StudioButtonStyle() }; cancel.Click += (_, _) => { Restore(); Close(); }; footer.Children.Add(cancel);
        var apply = new Button { Content = zh ? "应用" : "Apply", Background = Brush("#28766C"), Foreground = Brushes.White, Padding = new Thickness(18, 8, 18, 8), Style = StudioButtonStyle() };
        apply.Click += (_, _) => Apply(zh); footer.Children.Add(apply);
        sampleBorder.Background = Brush(_working.Toolbar); sampleBorder.BorderBrush = Brush(_working.Muted);
        RefreshSwatches(); Closing += (_, _) => { if (!Applied) Restore(); };
    }

    private Button PresetButton(ViewerTheme t)
    {
        var stack = new StackPanel(); var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
        foreach (var color in new[] { t.Shell, t.Toolbar, t.Accent }) chips.Children.Add(new Border { Width = 26, Height = 15, Background = Brush(color), BorderBrush = Brush(t.Muted), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0) });
        stack.Children.Add(chips); stack.Children.Add(new TextBlock { Text = ViewerTheme.LocalizedName(t.Name, _settings.Language == "zh"), HorizontalAlignment = HorizontalAlignment.Center });
        var b = new Button { Content = stack, Background = Brush(t.Shell), Foreground = Brush(t.Text), BorderBrush = Brush(t.Muted), BorderThickness = new Thickness(1), Padding = new Thickness(9), Margin = new Thickness(3), MinHeight = 58, Style = StudioButtonStyle() };
        AutomationProperties.SetName(b, ViewerTheme.LocalizedName(t.Name, _settings.Language == "zh")); b.Click += (_, _) => SetTheme(t); return b;
    }
    private void SetTheme(ViewerTheme t) { _working = t; _updating = true; _name.Text = ViewerTheme.LocalizedName(t.Name, _settings.Language == "zh"); _updating = false; _previewTheme(t); RefreshSwatches(); }
    private string GetPart() => _colorPart.SelectedIndex switch { 1 => _working.Toolbar, 2 => _working.Text, 3 => _working.Muted, 4 => _working.Accent, 5 => _working.Canvas, _ => _working.Shell };
    private void SetPart(string value) => _working = _colorPart.SelectedIndex switch { 1 => _working with { Toolbar = value }, 2 => _working with { Text = value }, 3 => _working with { Muted = value }, 4 => _working with { Accent = value }, 5 => _working with { Canvas = value }, _ => _working with { Shell = value } };
    private void RefreshSwatches()
    {
        if (_swatches is null || _working is null) return; _swatches.Children.Clear(); var color = (Color)ColorConverter.ConvertFromString(GetPart());
        foreach (var hex in new[] { Mix(color, Colors.White, .35), color, Mix(color, Colors.Black, .22), Colors.White, Colors.Black, Color.FromRgb(40, 118, 108) })
        {
            var value = $"#{hex.R:X2}{hex.G:X2}{hex.B:X2}"; var button = new Button { Width = 31, Height = 28, Padding = new Thickness(0), Margin = new Thickness(3), Background = new SolidColorBrush(hex), BorderBrush = new SolidColorBrush(Color.FromRgb(80, 85, 90)), BorderThickness = new Thickness(1), ToolTip = value };
            AutomationProperties.SetName(button, value); button.Click += (_, _) => { SetPart(value); _previewTheme(_working); RefreshSwatches(); }; _swatches.Children.Add(button);
        }
        if (_hex is not null) _hex.Text = GetPart();
        if (_sample is not null)
        {
            _sample.Foreground = Brush(_working.Text);
            if (_sample.Parent is Border preview) { preview.Background = Brush(_working.Toolbar); preview.BorderBrush = Brush(_working.Muted); }
        }
    }
    private void ApplyHex(bool zh)
    {
        try { var raw = _hex.Text.Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(raw, "^#[0-9a-fA-F]{6}$")) throw new FormatException(); SetPart(raw.ToUpperInvariant()); _previewTheme(_working); RefreshSwatches(); }
        catch { MessageBox.Show(this, zh ? "颜色格式请使用 #RRGGBB，例如 #91D9C5。" : "Use #RRGGBB, for example #91D9C5.", zh ? "颜色格式" : "Color format", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private void Apply(bool zh)
    {
        try { _working = _working with { Name = _name.Text.Trim() }; _working.Validate(); }
        catch { MessageBox.Show(this, zh ? "请填写有效名称，并确认颜色格式为 #RRGGBB。" : "Enter a valid name and use #RRGGBB colors.", zh ? "无法应用" : "Cannot apply", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        _settings.Theme = _working; _settings.TextScale = _scale.Value; _settings.CompactToolbar = _compact.IsChecked == true; _settings.PersonalStyles = _workingStyles.ToList();
        Applied = true; _settings.Save(); Close();
    }
    private void Reset() { SetTheme(ViewerTheme.BuiltIn[0]); _scale.Value = 1; _compact.IsChecked = false; }
    private void Restore() { _previewTheme(_originalTheme); _previewTextScale(_originalScale); _previewCompact(_originalCompact); }
    private void ShowLibrary(bool zh)
    {
        var window = new Window { Owner = this, Title = zh ? "个人风格" : "My styles", Width = 360, Height = 330, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background, Foreground = Foreground };
        window.Resources["HoverBrush"] = Resources["HoverBrush"]; window.Resources["PressedBrush"] = Resources["PressedBrush"];
        var panel = new DockPanel { Margin = new Thickness(14) }; window.Content = panel; var list = new ListBox();
        foreach (var style in _workingStyles) list.Items.Add(style.Name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(list);
        void button(string label, Action action) { var b = new Button { Content = label, Margin = new Thickness(3), Style = StudioButtonStyle() }; b.Click += (_, _) => action(); buttons.Children.Add(b); }
        button(zh ? "保存当前" : "Save current", () => { try { var style = _working with { Name = _name.Text.Trim() }; style.Validate(); _workingStyles.RemoveAll(x => x.Name == style.Name); _workingStyles.Add(style); list.Items.Clear(); foreach (var item in _workingStyles) list.Items.Add(item.Name); } catch { MessageBox.Show(window, zh ? "请先填写有效名称。" : "Enter a valid name first."); } });
        button(zh ? "载入" : "Load", () => { if (list.SelectedItem is string n && _workingStyles.FirstOrDefault(x => x.Name == n) is { } s) SetTheme(s); });
        button(zh ? "删除" : "Delete", () => { if (list.SelectedItem is string n) { _workingStyles.RemoveAll(x => x.Name == n); list.Items.Remove(n); } }); window.ShowDialog();
    }
    private static Color Mix(Color c, Color target, double amount) => Color.FromRgb((byte)(c.R * (1 - amount) + target.R * amount), (byte)(c.G * (1 - amount) + target.G * amount), (byte)(c.B * (1 - amount) + target.B * amount));
    private static SolidColorBrush Brush(string c) => new((Color)ColorConverter.ConvertFromString(c));
    private static Style StudioButtonStyle()
    {
        var appStyle = Application.Current?.TryFindResource(typeof(Button)) as Style;
        var style = appStyle is null ? new Style(typeof(Button)) : new Style(typeof(Button), appStyle);
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush("#20242A")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#FFFFFF")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#B8BEC5")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        return style;
    }
}
