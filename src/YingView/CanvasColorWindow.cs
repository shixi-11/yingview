using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace YingView;

internal static class CanvasColor
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = "";
        var raw = value?.Trim();
        if (raw?.StartsWith('#') == true) raw = raw[1..];
        if (raw is null || !Regex.IsMatch(raw, "\\A[0-9a-fA-F]{6}\\z")) return false;
        normalized = "#" + raw.ToUpperInvariant();
        return true;
    }

    public static Brush ReadableInk(Color color)
    {
        static double Linear(byte channel) { var v = channel / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        var luminance = .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        return luminance > .179 ? Brushes.Black : Brushes.White;
    }

    public static Color FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - c;
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x)
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}

internal sealed class CanvasColorWindow : Window
{
    private readonly TextBox _hex = new() { Name = "ColorHex", MaxLength = 16, Width = 148, Padding = new Thickness(10, 8, 10, 8), FontSize = 15, FontFamily = new FontFamily("Consolas"), Background = Brushes.White, Foreground = Brushes.Black };
    private readonly Button _apply;
    private readonly Border _preview = new() { Width = 50, Height = 36, CornerRadius = new CornerRadius(5), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock _error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Slider _hue = new() { Name = "ColorHue", Minimum = 0, Maximum = 359.9, SmallChange = 1, LargeChange = 15, Margin = new Thickness(0, 8, 0, 12) };
    private readonly ColorField _field = new() { Name = "ColorArea", Height = 178, Focusable = true, Margin = new Thickness(0, 0, 0, 12) };
    private bool _refreshing;
    public string SelectedColor { get; private set; }
    public event Action<string>? PreviewColorChanged;

    public CanvasColorWindow(bool chinese, string initialColor)
    {
        string T(string zh, string en) => chinese ? zh : en;
        Title = T("自定义背景颜色", "Custom background color");
        Width = 380; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 13;
        SetResourceReference(BackgroundProperty, "ShellBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        SelectedColor = CanvasColor.TryNormalize(initialColor, out var valid) ? valid : "#D6E4EF";
        var panel = new StackPanel { Margin = new Thickness(22) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = T("选择背景颜色", "Choose a background color"), FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        AutomationProperties.SetName(_field, T("颜色区域：方向键调整饱和度和明度", "Color area: arrow keys adjust saturation and brightness"));
        panel.Children.Add(_field);
        var hueStrip = new Border { Name = "HueStrip", Height = 14, Cursor = Cursors.Hand, CornerRadius = new CornerRadius(4), Background = new LinearGradientBrush(new GradientStopCollection {
            new(Colors.Red, 0), new(Colors.Yellow, 1d/6), new(Colors.Lime, 2d/6), new(Colors.Cyan, .5), new(Colors.Blue, 4d/6), new(Colors.Magenta, 5d/6), new(Colors.Red, 1) }, 0) };
        void DragHue(MouseEventArgs e) => _hue.Value = Math.Clamp(e.GetPosition(hueStrip).X / hueStrip.ActualWidth, 0, 1) * _hue.Maximum;
        hueStrip.MouseLeftButtonDown += (_, e) => { hueStrip.CaptureMouse(); DragHue(e); e.Handled = true; };
        hueStrip.MouseMove += (_, e) => { if (hueStrip.IsMouseCaptured) DragHue(e); };
        hueStrip.MouseLeftButtonUp += (_, e) => { hueStrip.ReleaseMouseCapture(); e.Handled = true; };
        panel.Children.Add(hueStrip);
        AutomationProperties.SetName(_hue, T("色相", "Hue")); panel.Children.Add(_hue);
        var colors = new UniformGrid { Name = "ColorSwatches", Columns = 8, Margin = new Thickness(0, 0, 0, 16) };
        foreach (var hex in new[] { "#FFFFFF", "#F3E9D2", "#D6E4EF", "#B7D7C8", "#D8C9EC", "#E8BCBB", "#808080", "#000000" })
        {
            var swatch = new Button { Width = 30, Height = 28, Background = ViewerTheme.Brush(hex), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), ToolTip = hex, Style = new Style(typeof(Button)), Padding = new Thickness(0) };
            AutomationProperties.SetName(swatch, T("颜色 ", "Color ") + hex);
            swatch.Click += (_, _) => SetColor(hex); colors.Children.Add(swatch);
        }
        panel.Children.Add(colors);
        panel.Children.Add(new TextBlock { Text = T("色号", "HEX color"), Margin = new Thickness(0, 0, 0, 6) });
        var entry = new StackPanel { Orientation = Orientation.Horizontal };
        entry.Children.Add(_preview); entry.Children.Add(_hex); panel.Children.Add(entry);
        AutomationProperties.SetName(_hex, T("颜色色号，例如 #D6E4EF", "Color hex, for example #D6E4EF"));
        _error.Text = T("请输入 6 位色号，例如 #D6E4EF。", "Enter a 6-digit color, for example #D6E4EF.");
        _error.SetResourceReference(ForegroundProperty, "TextBrush");
        panel.Children.Add(_error);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = T("取消", "Cancel"), IsCancel = true, Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(0, 0, 8, 0) };
        _apply = new Button { Name = "ApplyColor", Content = T("使用颜色", "Use color"), IsDefault = true, Padding = new Thickness(16, 9, 16, 9) };
        _apply.SetResourceReference(BackgroundProperty, "AccentBrush"); _apply.SetResourceReference(ForegroundProperty, "AccentTextBrush");
        _apply.Click += (_, _) => { if (CanvasColor.TryNormalize(_hex.Text, out var color)) { SelectedColor = color; DialogResult = true; } };
        footer.Children.Add(cancel); footer.Children.Add(_apply); panel.Children.Add(footer);
        _hue.ValueChanged += (_, _) => { if (!_refreshing) { _field.Hue = _hue.Value; _field.InvalidateVisual(); UpdateFromField(); } };
        _field.Changed += UpdateFromField;
        _hex.TextChanged += (_, _) =>
        {
            if (_refreshing) return;
            var ok = CanvasColor.TryNormalize(_hex.Text, out var color);
            _apply.IsEnabled = ok; _error.Visibility = ok ? Visibility.Hidden : Visibility.Visible;
            if (ok) SetColor(color, false);
        };
        SetColor(SelectedColor);
    }

    private void SetColor(string hex, bool updateText = true)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        var hue = delta == 0 ? _hue.Value : max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        _refreshing = true;
        _hue.Value = hue; _field.Hue = hue; _field.Saturation = max == 0 ? 0 : delta / max; _field.Value = max;
        if (updateText) _hex.Text = hex;
        _preview.Background = new SolidColorBrush(color); _apply.IsEnabled = true; _error.Visibility = Visibility.Hidden;
        _refreshing = false; _field.InvalidateVisual();
        PreviewColorChanged?.Invoke(hex);
    }

    private void UpdateFromField()
    {
        var color = CanvasColor.FromHsv(_field.Hue, _field.Saturation, _field.Value);
        _refreshing = true; _hex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _preview.Background = new SolidColorBrush(color); _apply.IsEnabled = true; _error.Visibility = Visibility.Hidden; _refreshing = false;
        PreviewColorChanged?.Invoke(_hex.Text);
    }

    private sealed class ColorField : FrameworkElement
    {
        public double Hue { get; set; }
        public double Saturation { get; set; }
        public double Value { get; set; }
        public event Action? Changed;
        public ColorField()
        {
            Cursor = Cursors.Cross;
            MouseLeftButtonDown += (_, e) => { Focus(); CaptureMouse(); Select(e.GetPosition(this)); e.Handled = true; };
            MouseMove += (_, e) => { if (IsMouseCaptured) Select(e.GetPosition(this)); };
            MouseLeftButtonUp += (_, e) => { if (IsMouseCaptured) { ReleaseMouseCapture(); e.Handled = true; } };
        }
        private void Select(Point p)
        {
            Saturation = Math.Clamp(p.X / ActualWidth, 0, 1); Value = 1 - Math.Clamp(p.Y / ActualHeight, 0, 1);
            InvalidateVisual(); Changed?.Invoke();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .1 : .01;
            switch (e.Key) { case Key.Left: Saturation = Math.Max(0, Saturation - step); break; case Key.Right: Saturation = Math.Min(1, Saturation + step); break; case Key.Up: Value = Math.Min(1, Value + step); break; case Key.Down: Value = Math.Max(0, Value - step); break; default: base.OnKeyDown(e); return; }
            e.Handled = true; InvalidateVisual(); Changed?.Invoke();
        }
        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(RenderSize);
            dc.DrawRectangle(new LinearGradientBrush(Colors.White, CanvasColor.FromHsv(Hue, 1, 1), 0), null, rect);
            dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent, Colors.Black, 90), null, rect);
            var p = new Point(Saturation * ActualWidth, (1 - Value) * ActualHeight);
            dc.DrawEllipse(null, new Pen(Brushes.Black, 3), p, 5, 5); dc.DrawEllipse(null, new Pen(Brushes.White, 1.5), p, 5, 5);
        }
    }
}
