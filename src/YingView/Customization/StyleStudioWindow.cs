using System.Windows;
using System.Windows.Data;
using System.Windows.Shapes;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace YingView;

/// <summary>A small, live-preview editor for the viewer's appearance.</summary>
public sealed class StyleStudioWindow : Window
{
    private static readonly Brush WindowBrush = Solid("#F3F5F4");
    private static readonly Brush SurfaceBrush = Solid("#FFFFFF");
    private static readonly Brush InkBrush = Solid("#20282A");
    private static readonly Brush MutedBrush = Solid("#657174");
    private static readonly Brush LineBrush = Solid("#DCE3E1");
    private static readonly Brush AccentBrush = Solid("#28766C");

    private readonly Preferences _settings;
    private readonly Action<ViewerTheme> _previewTheme;
    private readonly Action<double> _previewTextScale;
    private readonly Action<bool> _previewCompact;
    private readonly bool _zh;
    private readonly ViewerTheme _originalTheme;
    private readonly double _originalScale;
    private readonly bool _originalCompact;
    private readonly List<ViewerTheme> _workingStyles;
    private readonly List<(ViewerTheme Theme, Button Card)> _presetCards = [];
    private ViewerTheme _working;
    private ComboBox _colorPart = null!;
    private TextBox _name = null!, _hex = null!;
    private Slider _scale = null!;
    private CheckBox _compact = null!;
    private TextBlock _sample = null!, _scaleValue = null!;
    private StackPanel _swatches = null!;
    private bool _updating;

    public bool Applied { get; private set; }

    public StyleStudioWindow(Preferences settings, Action<ViewerTheme> previewTheme, Action<double> previewTextScale, Action<bool> previewCompact)
    {
        _settings = settings;
        _previewTheme = previewTheme;
        _previewTextScale = previewTextScale;
        _previewCompact = previewCompact;
        _zh = settings.Language == "zh";
        _originalTheme = settings.Theme;
        _working = settings.Theme;
        _originalScale = settings.TextScale;
        _originalCompact = settings.CompactToolbar;
        _workingStyles = settings.PersonalStyles.ToList();

        Title = _zh ? "外观" : "Appearance";
        Width = Math.Min(704, SystemParameters.WorkArea.Width);
        MinWidth = Math.Min(620, SystemParameters.WorkArea.Width);
        MaxWidth = Math.Min(780, SystemParameters.WorkArea.Width);
        Height = Math.Min(760, SystemParameters.WorkArea.Height - 32);
        MinHeight = Math.Min(610, Height);
        MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WindowBrush;
        Foreground = InkBrush;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        Resources["HoverBrush"] = Solid("#EDF3F1");
        Resources["PressedBrush"] = Solid("#DFEAE6");

        var shell = new DockPanel { Background = WindowBrush };
        Content = shell;

        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        shell.Children.Add(footer);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(24, 22, 24, 16),
            CanContentScroll = false
        };
        shell.Children.Add(scroll);
        scroll.Content = BuildBody();

        RefreshSwatches();
        RefreshPresetCards();
        Closing += (_, _) => { if (!Applied) Restore(); };
    }

    private UIElement BuildBody()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = _zh ? "把界面调成你喜欢的样子。" : "Make the viewer feel like yours.",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = InkBrush,
            Margin = new Thickness(0, 0, 0, 4)
        });
        body.Children.Add(new TextBlock
        {
            Text = _zh ? "选择一套风格，再调整颜色、文字大小和工具栏。" : "Choose a look, then adjust color, text size, and toolbar density.",
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20)
        });

        body.Children.Add(SectionLabel(_zh ? "主题" : "Theme"));
        var presetGrid = new UniformGrid { Columns = 2, Margin = new Thickness(-5, 5, -5, 18) };
        foreach (var preset in ViewerTheme.BuiltIn)
        {
            var card = BuildPresetCard(preset);
            _presetCards.Add((preset, card));
            presetGrid.Children.Add(card);
        }
        body.Children.Add(presetGrid);

        body.Children.Add(SectionLabel(_zh ? "风格名称" : "Style name"));
        _name = new TextBox
        {
            Text = ViewerTheme.LocalizedName(_working.Name, _zh),
            MaxLength = 60,
            Padding = new Thickness(11, 8, 11, 8),
            Margin = new Thickness(0, 6, 0, 16),
            Background = SurfaceBrush,
            Foreground = InkBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            Style = RoundedTextBoxStyle()
        };
        AutomationProperties.SetName(_name, _zh ? "风格名称" : "Style name");
        _name.TextChanged += (_, _) =>
        {
            if (_updating) return;
            _working = _working with { Name = _name.Text.Trim() };
            _previewTheme(_working);
            RefreshPresetCards();
        };
        body.Children.Add(_name);

        body.Children.Add(SectionLabel(_zh ? "颜色" : "Color"));
        body.Children.Add(new TextBlock
        {
            Text = _zh ? "选择要调整的界面部分，再点选颜色。" : "Choose an area of the interface, then pick a color.",
            Foreground = MutedBrush,
            Margin = new Thickness(0, 3, 0, 7)
        });
        var colorPanel = new Border
        {
            Background = SurfaceBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 15)
        };
        var colorRows = new StackPanel();
        colorPanel.Child = colorRows;
        var colorRow = new DockPanel { LastChildFill = true };
        _colorPart = new ComboBox { Width = 152, SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center,
            Background = SurfaceBrush, Foreground = InkBrush, BorderBrush = LineBrush };
        foreach (var part in _zh
                     ? new[] { "窗口背景", "工具栏", "正文文字", "辅助文字", "强调色", "画布背景" }
                     : new[] { "Window", "Toolbar", "Text", "Muted text", "Accent", "Canvas" })
            _colorPart.Items.Add(part);
        _colorPart.SelectionChanged += (_, _) => RefreshSwatches();
        DockPanel.SetDock(_colorPart, Dock.Left);
        colorRow.Children.Add(_colorPart);
        _swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        colorRow.Children.Add(_swatches);
        colorRows.Children.Add(colorRow);

        var customRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false };
        var customHint = new TextBlock
        {
            Text = _zh ? "也可以输入颜色值" : "Or enter a color value",
            Foreground = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        customRow.Children.Add(customHint);
        var hexPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var chooseColor = new Button
        {
            Content = _zh ? "自定义…" : "Custom…",
            Margin = new Thickness(0, 0, 7, 0),
            Padding = new Thickness(10, 5, 10, 5),
            Style = ButtonStyle()
        };
        chooseColor.Click += (_, _) => ShowCustomColorPicker();
        hexPanel.Children.Add(chooseColor);
        _hex = new TextBox
        {
            Width = 96,
            Padding = new Thickness(8, 5, 8, 5),
            Background = SurfaceBrush,
            Foreground = InkBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            ToolTip = "#RRGGBB",
            Style = RoundedTextBoxStyle()
        };
        AutomationProperties.SetName(_hex, _zh ? "十六进制颜色值" : "Hex color value");
        var setHex = new Button
        {
            Content = _zh ? "使用" : "Use",
            Margin = new Thickness(7, 0, 0, 0),
            Padding = new Thickness(12, 5, 12, 5),
            Style = ButtonStyle()
        };
        setHex.Click += (_, _) => ApplyHex();
        hexPanel.Children.Add(_hex);
        hexPanel.Children.Add(setHex);
        DockPanel.SetDock(hexPanel, Dock.Right);
        customRow.Children.Add(hexPanel);
        colorRows.Children.Add(customRow);
        body.Children.Add(colorPanel);

        var settingsGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var scaleTitle = SectionLabel(_zh ? "文字大小" : "Text size");
        settingsGrid.Children.Add(scaleTitle);
        _scaleValue = new TextBlock { Foreground = MutedBrush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_scaleValue, 1);
        settingsGrid.Children.Add(_scaleValue);
        _scale = new Slider
        {
            Minimum = 0.85,
            Maximum = 1.3,
            Value = Math.Clamp(_settings.TextScale, .85, 1.3),
            TickFrequency = .05,
            IsSnapToTickEnabled = false,
            IsMoveToPointEnabled = true,
            Margin = new Thickness(0, 4, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = true
        };
        AutomationProperties.SetName(_scale, _zh ? "界面文字大小" : "Interface text size");
        _scale.ValueChanged += (_, _) =>
        {
            if (_sample is not null) _sample.FontSize = 14 * _scale.Value;
            _scaleValue.Text = $"{_scale.Value:P0}";
            _previewTextScale(_scale.Value);
        };
        Grid.SetRow(_scale, 1);
        settingsGrid.Children.Add(_scale);
        _compact = new CheckBox
        {
            Content = _zh ? "紧凑工具栏" : "Compact toolbar",
            IsChecked = _settings.CompactToolbar,
            Margin = new Thickness(0, 9, 0, 0),
            Foreground = InkBrush,
            Focusable = true
        };
        AutomationProperties.SetName(_compact, _zh ? "紧凑工具栏" : "Compact toolbar");
        _compact.Checked += (_, _) => _previewCompact(true);
        _compact.Unchecked += (_, _) => _previewCompact(false);
        Grid.SetRow(_compact, 2);
        settingsGrid.Children.Add(_compact);
        body.Children.Add(settingsGrid);

        var previewCard = new Border
        {
            Background = SurfaceBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 4)
        };
        var previewStack = new StackPanel();
        previewStack.Children.Add(new TextBlock
        {
            Text = _zh ? "文字预览" : "Text preview",
            Foreground = MutedBrush,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _sample = new TextBlock
        {
            Text = _zh ? "在光线与细节之间，找到你喜欢的观看方式。" : "Find your preferred way to see every detail.",
            FontSize = 14 * _scale.Value,
            Foreground = Brush(_working.Text),
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(12, 10, 12, 10)
        };
        var sampleBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush(_working.Muted),
            Background = Brush(_working.Toolbar),
            Child = _sample
        };
        previewStack.Children.Add(sampleBorder);
        previewCard.Child = previewStack;
        body.Children.Add(previewCard);
        _scaleValue.Text = $"{_scale.Value:P0}";
        return body;
    }

    private Button BuildPresetCard(ViewerTheme theme)
    {
        var b = new Button
        {
            Padding = new Thickness(0),
            Margin = new Thickness(5),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Style = ButtonStyle()
        };
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = LineBrush,
            Background = SurfaceBrush,
            Padding = new Thickness(10)
        };
        var cardStack = new StackPanel();
        var preview = new Border
        {
            Height = 46,
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush(theme.Muted),
            Background = Brush(theme.Shell),
            ClipToBounds = true
        };
        var previewGrid = new Grid();
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        previewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var topBar = new Border { Background = Brush(theme.Toolbar) };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 3, 0, 0) };
        foreach (var dotColor in new[] { theme.Muted, theme.Accent, theme.Muted })
            dots.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = Brush(dotColor), Margin = new Thickness(0, 0, 4, 0) });
        topBar.Child = dots;
        previewGrid.Children.Add(topBar);
        var canvas = new Grid { Background = Brush(theme.Canvas), Margin = new Thickness(0, 0, 0, 0) };
        var sampleImage = new Border
        {
            Width = 30,
            Height = 21,
            CornerRadius = new CornerRadius(3),
            Background = Brush(theme.Toolbar),
            BorderBrush = Brush(theme.Accent),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        var miniLine = new Border
        {
            Width = 55,
            Height = 3,
            CornerRadius = new CornerRadius(2),
            Background = Brush(theme.Text),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(47, 0, 0, 0)
        };
        canvas.Children.Add(sampleImage);
        canvas.Children.Add(miniLine);
        Grid.SetRow(canvas, 1);
        previewGrid.Children.Add(canvas);
        preview.Child = previewGrid;
        cardStack.Children.Add(preview);

        var nameRow = new DockPanel { Margin = new Thickness(1, 8, 1, 0), LastChildFill = true };
        var presetName = new TextBlock
        {
            Text = ViewerTheme.LocalizedName(theme.Name, _zh),
            FontWeight = FontWeights.SemiBold,
            Foreground = InkBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        nameRow.Children.Add(presetName);
        var selected = new TextBlock
        {
            Text = _zh ? "✓ 已选" : "✓ Selected",
            Foreground = AccentBrush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        DockPanel.SetDock(selected, Dock.Right);
        nameRow.Children.Add(selected);
        cardStack.Children.Add(nameRow);
        card.Child = cardStack;
        b.Content = card;
        AutomationProperties.SetName(b, ViewerTheme.LocalizedName(theme.Name, _zh));
        b.Click += (_, _) => SetTheme(theme);
        // Keep the selection badge attached to the card for refreshes.
        b.Tag = selected;
        return b;
    }

    private StackPanel BuildFooter()
    {
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = WindowBrush,
            Margin = new Thickness(22, 12, 24, 18)
        };
        var reset = SecondaryButton(_zh ? "恢复默认" : "Reset", Reset);
        reset.Margin = new Thickness(0, 0, 8, 0);
        footer.Children.Add(reset);
        var library = SecondaryButton(_zh ? "我的风格" : "My styles", ShowLibrary);
        library.Margin = new Thickness(0, 0, 12, 0);
        footer.Children.Add(library);
        var cancel = SecondaryButton(_zh ? "取消" : "Cancel", () => { Restore(); Close(); });
        cancel.Margin = new Thickness(0, 0, 8, 0);
        footer.Children.Add(cancel);
        var apply = new Button
        {
            Content = _zh ? "应用" : "Apply",
            Background = AccentBrush,
            Foreground = Brushes.White,
            BorderBrush = AccentBrush,
            Padding = new Thickness(19, 8, 19, 8),
            MinWidth = 88,
            Style = ButtonStyle(true)
        };
        apply.Click += (_, _) => Apply();
        AutomationProperties.SetName(apply, _zh ? "应用外观" : "Apply appearance");
        footer.Children.Add(apply);
        return footer;
    }

    private void RefreshPresetCards()
    {
        foreach (var (theme, card) in _presetCards)
        {
            var isActive = string.Equals(theme.Name, _working.Name, StringComparison.OrdinalIgnoreCase) &&
                           theme.Shell == _working.Shell && theme.Toolbar == _working.Toolbar &&
                           theme.Text == _working.Text && theme.Muted == _working.Muted &&
                           theme.Accent == _working.Accent && theme.Canvas == _working.Canvas;
            if (card.Content is Border cardBorder)
            {
                cardBorder.BorderBrush = isActive ? AccentBrush : LineBrush;
                cardBorder.BorderThickness = new Thickness(isActive ? 2 : 1);
            }
            if (card.Tag is TextBlock badge) badge.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private UIElement SectionLabel(string text) => new TextBlock
    {
        Text = text,
        Foreground = InkBrush,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold
    };

    private Button SecondaryButton(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(13, 8, 13, 8), MinWidth = 76, Style = ButtonStyle() };
        button.Click += (_, _) => action();
        return button;
    }

    private void SetTheme(ViewerTheme theme)
    {
        _working = theme;
        _updating = true;
        _name.Text = ViewerTheme.LocalizedName(theme.Name, _zh);
        _updating = false;
        _previewTheme(theme);
        RefreshSwatches();
        RefreshPresetCards();
    }

    private string GetPart() => _colorPart.SelectedIndex switch
    {
        1 => _working.Toolbar,
        2 => _working.Text,
        3 => _working.Muted,
        4 => _working.Accent,
        5 => _working.Canvas,
        _ => _working.Shell
    };

    private void SetPart(string value) => _working = _colorPart.SelectedIndex switch
    {
        1 => _working with { Toolbar = value },
        2 => _working with { Text = value },
        3 => _working with { Muted = value },
        4 => _working with { Accent = value },
        5 => _working with { Canvas = value },
        _ => _working with { Shell = value }
    };

    private void RefreshSwatches()
    {
        if (_swatches is null || _working is null) return;
        _swatches.Children.Clear();
        var current = (Color)ColorConverter.ConvertFromString(GetPart());
        var colors = new[]
        {
            Mix(current, Colors.White, .32), current, Mix(current, Colors.Black, .22),
            Color.FromRgb(40, 118, 108), Color.FromRgb(119, 155, 211), Color.FromRgb(199, 142, 113)
        };
        foreach (var color in colors)
        {
            var value = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            var button = new Button
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Margin = new Thickness(3, 0, 3, 0),
                Background = new SolidColorBrush(color),
                BorderBrush = current == color ? AccentBrush : LineBrush,
                BorderThickness = new Thickness(current == color ? 2 : 1),
                Style = ColorButtonStyle()
            };
            button.ToolTip = value;
            AutomationProperties.SetName(button, $"{(_zh ? "颜色" : "Color")} {value}");
            button.Click += (_, _) =>
            {
                SetPart(value);
                _previewTheme(_working);
                RefreshSwatches();
                RefreshPresetCards();
            };
            _swatches.Children.Add(button);
        }
        if (_hex is not null) _hex.Text = GetPart();
        if (_sample is not null)
        {
            _sample.Foreground = Brush(_working.Text);
            if (_sample.Parent is Border preview)
            {
                preview.Background = Brush(_working.Toolbar);
                preview.BorderBrush = Brush(_working.Muted);
            }
        }
    }

    private void ApplyHex()
    {
        var raw = _hex.Text.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(raw, "^#[0-9a-fA-F]{6}$"))
        {
            MessageBox.Show(this,
                _zh ? "请使用 #RRGGBB 格式，例如 #91D9C5。" : "Use #RRGGBB format, for example #91D9C5.",
                _zh ? "颜色值" : "Color value", MessageBoxButton.OK, MessageBoxImage.Information);
            _hex.Focus();
            return;
        }
        SetPart(raw.ToUpperInvariant());
        _previewTheme(_working);
        RefreshSwatches();
        RefreshPresetCards();
    }

    private void ShowCustomColorPicker()
    {
        var options = new[]
        {
            "#111417", "#303942", "#59656B", "#87969A", "#DDE4E2",
            "#FFFFFF", "#28766C", "#4F9B8B", "#91D9C5", "#B0D1A0",
            "#3F6E9E", "#779BD3", "#AFC8E8", "#73558F", "#C8AFE9",
            "#A65A4B", "#C77A66", "#D8A24C", "#E6C981", "#26282B"
        };
        var selected = GetPart();
        var dialog = new Window
        {
            Owner = this,
            Title = _zh ? "自定义颜色" : "Custom color",
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = WindowBrush,
            Foreground = InkBrush,
            FontFamily = FontFamily,
            FontSize = 13
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        dialog.Content = panel;
        panel.Children.Add(new TextBlock
        {
            Text = _zh ? "选择一个颜色" : "Choose a color",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var choices = new UniformGrid { Columns = 5 };
        foreach (var value in options)
        {
            var color = Brush(value);
            var button = new Button
            {
                Width = 44,
                Height = 38,
                Margin = new Thickness(3),
                Background = color,
                BorderBrush = value.Equals(selected, StringComparison.OrdinalIgnoreCase) ? AccentBrush : LineBrush,
                BorderThickness = new Thickness(value.Equals(selected, StringComparison.OrdinalIgnoreCase) ? 2 : 1),
                ToolTip = value,
                Style = ColorButtonStyle()
            };
            AutomationProperties.SetName(button, $"{(_zh ? "颜色" : "Color")} {value}");
            button.Click += (_, _) =>
            {
                selected = value;
                foreach (var item in choices.Children.OfType<Button>())
                {
                    var active = Equals(item.ToolTip, selected);
                    item.BorderBrush = active ? AccentBrush : LineBrush;
                    item.BorderThickness = new Thickness(active ? 2 : 1);
                }
            };
            choices.Children.Add(button);
        }
        panel.Children.Add(choices);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        panel.Children.Add(footer);
        var cancel = SecondaryButton(_zh ? "取消" : "Cancel", () => dialog.DialogResult = false);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        footer.Children.Add(cancel);
        var use = new Button { Content = _zh ? "使用颜色" : "Use color", Padding = new Thickness(13, 7, 13, 7), Style = ButtonStyle(true) };
        use.Click += (_, _) => dialog.DialogResult = true;
        footer.Children.Add(use);
        if (dialog.ShowDialog() == true)
        {
            SetPart(selected);
            _previewTheme(_working);
            RefreshSwatches();
            RefreshPresetCards();
        }
    }

    private void Apply()
    {
        var candidate = _working with { Name = _name.Text.Trim() };
        try { candidate.Validate(); }
        catch
        {
            MessageBox.Show(this,
                _zh ? "请填写风格名称，并确认颜色值使用 #RRGGBB 格式。" : "Enter a style name and use #RRGGBB color values.",
                _zh ? "无法应用" : "Cannot apply", MessageBoxButton.OK, MessageBoxImage.Information);
            _name.Focus();
            return;
        }

        var oldTheme = _settings.Theme;
        var oldScale = _settings.TextScale;
        var oldCompact = _settings.CompactToolbar;
        var oldStyles = _settings.PersonalStyles;
        _settings.Theme = candidate;
        _settings.TextScale = _scale.Value;
        _settings.CompactToolbar = _compact.IsChecked == true;
        _settings.PersonalStyles = _workingStyles.ToList();
        if (!_settings.Save())
        {
            _settings.Theme = oldTheme;
            _settings.TextScale = oldScale;
            _settings.CompactToolbar = oldCompact;
            _settings.PersonalStyles = oldStyles;
            MessageBox.Show(this,
                _zh ? "设置未能保存，请检查存储空间或文件权限后重试。" : "Settings could not be saved. Check available storage and file permissions, then try again.",
                _zh ? "保存失败" : "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _working = candidate;
        Applied = true;
        Close();
    }

    private void Reset()
    {
        SetTheme(ViewerTheme.BuiltIn[0]);
        _scale.Value = 1;
        _compact.IsChecked = false;
    }

    private void Restore()
    {
        _previewTheme(_originalTheme);
        _previewTextScale(_originalScale);
        _previewCompact(_originalCompact);
    }

    private void ShowLibrary()
    {
        var library = CreateLibraryWindow();
        library.Owner = this;
        library.ShowDialog();
    }

    internal Window CreateLibraryWindow()
    {
        var window = new Window
        {
            Title = _zh ? "我的风格" : "My styles",
            Width = 380,
            Height = 340,
            MinWidth = 340,
            MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = WindowBrush,
            Foreground = InkBrush,
            FontFamily = FontFamily,
            FontSize = 13
        };
        var panel = new DockPanel { Margin = new Thickness(18) };
        window.Content = panel;
        var list = new ListBox
        {
            Background = SurfaceBrush,
            BorderBrush = LineBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5)
        };
        void RefillList()
        {
            list.Items.Clear();
            foreach (var style in _workingStyles) list.Items.Add(style.Name);
        }
        RefillList();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(list);
        void AddButton(string label, Action action)
        {
            var button = SecondaryButton(label, action);
            button.Margin = new Thickness(0, 0, 7, 0);
            buttons.Children.Add(button);
        }
        AddButton(_zh ? "保存当前" : "Save current", () =>
        {
            var candidate = _working with { Name = _name.Text.Trim() };
            try
            {
                candidate.Validate();
                _workingStyles.RemoveAll(x => x.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase));
                _workingStyles.Add(candidate);
                RefillList();
            }
            catch { MessageBox.Show(window, _zh ? "请先填写有效名称。" : "Enter a valid name first."); }
        });
        AddButton(_zh ? "载入" : "Load", () =>
        {
            if (list.SelectedItem is string name && _workingStyles.FirstOrDefault(x => x.Name == name) is { } selected)
                SetTheme(selected);
        });
        AddButton(_zh ? "删除" : "Delete", () =>
        {
            if (list.SelectedItem is string name)
            {
                _workingStyles.RemoveAll(x => x.Name == name);
                RefillList();
            }
        });
        return window;
    }

    private static Color Mix(Color c, Color target, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount) + target.R * amount),
        (byte)(c.G * (1 - amount) + target.G * amount),
        (byte)(c.B * (1 - amount) + target.B * amount));

    private static SolidColorBrush Solid(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private static Style ButtonStyle(bool primary = false)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.ForegroundProperty, primary ? Brushes.White : InkBrush));
        style.Setters.Add(new Setter(Control.BackgroundProperty, primary ? AccentBrush : SurfaceBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, primary ? AccentBrush : LineBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
        style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate()));
        return style;
    }

    private static ControlTemplate ButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Chrome";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.SnapsToDevicePixelsProperty, true);
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
        content.SetBinding(ContentPresenter.ContentTemplateProperty, new System.Windows.Data.Binding("ContentTemplate") { RelativeSource = RelativeSource.TemplatedParent });
        content.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new System.Windows.Data.Binding("HorizontalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
        content.SetBinding(ContentPresenter.VerticalAlignmentProperty, new System.Windows.Data.Binding("VerticalContentAlignment") { RelativeSource = RelativeSource.TemplatedParent });
        content.SetBinding(ContentPresenter.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        border.AppendChild(content);
        template.VisualTree = border;
        template.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, Solid("#EDF3F1"), "Chrome") } });
        template.Triggers.Add(new Trigger { Property = ButtonBase.IsPressedProperty, Value = true, Setters = { new Setter(UIElement.OpacityProperty, .82) } });
        template.Triggers.Add(new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true, Setters = { new Setter(Border.BorderBrushProperty, AccentBrush, "Chrome"), new Setter(Border.BorderThicknessProperty, new Thickness(2), "Chrome") } });
        template.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(UIElement.OpacityProperty, .5) } });
        return template;
    }

    private static Style RoundedTextBoxStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        var template = new ControlTemplate(typeof(TextBox));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Chrome";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
        host.SetValue(ScrollViewer.MarginProperty, new Thickness(0));
        border.AppendChild(host);
        template.VisualTree = border;
        template.Triggers.Add(new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true, Setters = { new Setter(Border.BorderBrushProperty, AccentBrush, "Chrome"), new Setter(Border.BorderThicknessProperty, new Thickness(2), "Chrome") } });
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static Style ColorButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Chrome";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
        template.VisualTree = border;
        template.Triggers.Add(new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true, Setters = { new Setter(Border.BorderBrushProperty, AccentBrush, "Chrome"), new Setter(Border.BorderThicknessProperty, new Thickness(3), "Chrome") } });
        template.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(UIElement.OpacityProperty, .78) } });
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }
}
