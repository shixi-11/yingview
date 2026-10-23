using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using YingView.Core;
using YingView.Imaging;

namespace YingView.Review;

/// <summary>A local, manual two-image comparison workspace. It never writes to source images.</summary>
public sealed class ReviewWindow : Window
{
    private readonly string[] _files;
    private readonly bool _isZh;
    private readonly ComboBox[] _pickers = new ComboBox[2];
    private readonly ImageSurface[] _surfaces = new ImageSurface[2];
    private readonly TextBlock[] _dimensions = new TextBlock[2];
    private readonly TextBlock[] _decisions = new TextBlock[2];
    private readonly Button[][] _decisionButtons = [new Button[2], new Button[2]];
    private readonly int[] _loadGeneration = new int[2];
    private readonly SemaphoreSlim[] _loadGates = [new(1, 1), new(1, 1)];
    private readonly Dictionary<string, string> _marks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Brush[] _canvasBrushes = [new SolidColorBrush(Color.FromRgb(17, 20, 23)), Brushes.White, CheckerBrush()];
    private readonly string[] _backgroundNames;
    private int _backgroundIndex;
    private bool _syncing = true;
    private readonly bool[] _sideReady = new bool[2];
    private bool _closed;

    public ReviewWindow(string[] files, string current, bool isZh)
    {
        _isZh = isZh;
        _backgroundNames = isZh ? ["深色", "浅色", "棋盘"] : ["Dark", "Light", "Checker"];
        _files = (files ?? []).Where(ImageLoader.Supports).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (_files.Length < 2) throw new ArgumentException(isZh ? "至少需要两个受支持的图片文件。" : "At least two supported image files are required.", nameof(files));
        var start = Array.FindIndex(_files, p => string.Equals(p, SafeFullPath(current), StringComparison.OrdinalIgnoreCase));
        if (start < 0) start = 0;
        var second = (start + 1) % _files.Length;

        Title = T("对照审阅 · 映窗", "Compare images · YingView");
        Width = 1120; Height = 760; MinWidth = 900; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(17, 20, 23));
        Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        UseLayoutRounding = true;
        Resources["HoverBrush"] = BrushFrom("#303A3E");
        Resources["PressedBrush"] = BrushFrom("#202824");
        Content = BuildContent(start, second);
        Loaded += async (_, _) => await LoadPairAsync(start, second);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.F or System.Windows.Input.Key.D0 or System.Windows.Input.Key.NumPad0)
            { FitBoth(); e.Handled = true; }
        };
        Closed += (_, _) => { _closed = true; for (var i = 0; i < _loadGeneration.Length; i++) _loadGeneration[i]++; };
    }

    private const string Pending = "Pending";
    private const string Keep = "Keep";
    private string T(string zh, string en) => _isZh ? zh : en;

    private UIElement BuildContent(int leftIndex, int rightIndex)
    {
        var root = new Grid { Margin = new Thickness(16, 14, 16, 12), Background = Background };
        TextElement.SetForeground(root, Foreground);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(2, 0, 2, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock { Text = T("对照审阅", "Compare & review"), FontSize = 19, FontWeight = FontWeights.SemiBold });
        titleStack.Children.Add(new TextBlock { Text = T("同步查看细节，记录选择并导出清单", "View details together, record your choices, and export a list."), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = BrushFrom("#A9B1B8"), Margin = new Thickness(0, 3, 0, 0) });
        header.Children.Add(titleStack);
        var headerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var fit = MakeButton(T("完整显示", "Fit images"), subtle: true);
        fit.ToolTip = T("完整显示两张图片 · F", "Fit both images · F");
        fit.Click += (_, _) => FitBoth(); headerActions.Children.Add(fit);
        var syncToggle = MakeButton(T("同步视图：开", "Sync view: On"), subtle: true);
        syncToggle.Click += (_, _) =>
        {
            _syncing = !_syncing;
            syncToggle.Content = T(_syncing ? "同步视图：开" : "同步视图：关", _syncing ? "Sync view: On" : "Sync view: Off");
            if (_syncing) Synchronize(0);
        };
        var backgroundButton = MakeButton(_backgroundNames[0], subtle: true);
        backgroundButton.ToolTip = T("切换透明图的显示底色", "Cycle the transparency preview background");
        backgroundButton.Click += (_, _) => CycleBackground(backgroundButton);
        headerActions.Children.Add(syncToggle); headerActions.Children.Add(backgroundButton);
        Grid.SetColumn(headerActions, 1); header.Children.Add(headerActions);
        root.Children.Add(header);

        var comparison = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddPane(comparison, 0, leftIndex);
        AddPane(comparison, 2, rightIndex);
        Grid.SetRow(comparison, 1); root.Children.Add(comparison);

        var footer = new Grid { Margin = new Thickness(2, 2, 2, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var help = new TextBlock { Text = T("缩放：滚轮　移动：拖动画面　导出只保存本次人工标记", "Zoom: wheel　Pan: drag　Export includes only your review marks"), FontSize = 12, Foreground = BrushFrom("#A9B1B8"), VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(help);
        var export = MakeButton(T("导出 CSV 清单…", "Export CSV list…"), primary: true);
        export.Click += async (_, _) => await ExportCsvAsync();
        Grid.SetColumn(export, 1); footer.Children.Add(export);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        return root;
    }

    private void AddPane(Grid parent, int column, int initialIndex)
    {
        var pane = new Grid();
        pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var picker = new ComboBox { Height = 34, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 4, 8, 4), Background = Brushes.White, Foreground = BrushFrom("#20242A"), BorderBrush = BrushFrom("#525C62"), FontSize = 12 };
        picker.ItemsSource = BuildDisplayNames();
        picker.SelectedIndex = initialIndex;
        int side = column == 0 ? 0 : 1;
        picker.SelectionChanged += async (_, _) =>
        {
            if (picker.SelectedIndex < 0) return;
            var path = _files[picker.SelectedIndex];
            await LoadSideAsync(side, path);
        };
        _pickers[side] = picker;
        pane.Children.Add(picker);

        var surface = new ImageSurface { CanvasBrush = _canvasBrushes[_backgroundIndex], AlphaBackground = "dark" };
        surface.ViewChanged += () => { if (_syncing && !_syncingNow) Synchronize(side); };
        _surfaces[side] = surface;
        var border = new Border { Background = BrushFrom("#0C0E10"), BorderBrush = BrushFrom("#333A41"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = surface };
        Grid.SetRow(border, 1); pane.Children.Add(border);

        var status = new Grid { Margin = new Thickness(1, 8, 1, 0) };
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var leftStatus = new StackPanel();
        var decisionText = new TextBlock { Text = T("待定", "Pending"), FontSize = 12, Foreground = BrushFrom("#A9B1B8") };
        var dimensions = new TextBlock { Text = "", FontSize = 11, Foreground = BrushFrom("#8E979E"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        leftStatus.Children.Add(decisionText); leftStatus.Children.Add(dimensions);
        status.Children.Add(leftStatus);
        var choices = new StackPanel { Orientation = Orientation.Horizontal };
        var keep = MakeButton(T("保留", "Keep"), subtle: true);
        keep.Click += (_, _) => SetMark(side, Keep);
        var pending = MakeButton(T("待定", "Pending"), subtle: true);
        pending.Click += (_, _) => SetMark(side, Pending);
        _decisionButtons[side][0] = keep; _decisionButtons[side][1] = pending;
        choices.Children.Add(keep); choices.Children.Add(pending);
        Grid.SetColumn(choices, 1); status.Children.Add(choices);
        Grid.SetRow(status, 2); pane.Children.Add(status);
        _dimensions[side] = dimensions; _decisions[side] = decisionText;
        Grid.SetColumn(pane, column); parent.Children.Add(pane);
    }

    private bool _syncingNow;
    private void Synchronize(int sourceSide)
    {
        if (!_syncing || _syncingNow || _surfaces.Any(s => s?.Document is null)) return;
        var srcSurface = _surfaces[sourceSide];
        var src = srcSurface.View;
        var dstSurface = _surfaces[1 - sourceSide];
        var dst = dstSurface.View;
        if (src.ViewportWidth <= 0 || src.ViewportHeight <= 0 || dst.ViewportWidth <= 0 || dst.ViewportHeight <= 0) return;
        _syncingNow = true;
        try
        {
            dstSurface.Fit(ViewportFitMode.Contain);
            var srcBase = FitScale(srcSurface);
            var dstBase = FitScale(dstSurface);
            var relativeZoom = src.Scale / srcBase;
            if (Math.Abs(relativeZoom - 1) < 0.000001) return;
            var targetScale = dstBase * relativeZoom;
            dst.ZoomAt(targetScale / dst.Scale, dst.ViewportWidth / 2, dst.ViewportHeight / 2);
            var targetX = MapPan(src.OffsetX, src.DisplayWidth, src.ViewportWidth, dst.DisplayWidth, dst.ViewportWidth);
            var targetY = MapPan(src.OffsetY, src.DisplayHeight, src.ViewportHeight, dst.DisplayHeight, dst.ViewportHeight);
            dst.Pan(targetX - dst.OffsetX, targetY - dst.OffsetY);
        }
        finally { _syncingNow = false; }
    }

    private void FitBoth()
    {
        _syncingNow = true;
        try { foreach (var surface in _surfaces) surface.Fit(ViewportFitMode.Contain); }
        finally { _syncingNow = false; }
    }

    private static double FitScale(ImageSurface surface)
    {
        var doc = surface.Document!;
        return Math.Min(surface.View.ViewportWidth / doc.Width, surface.View.ViewportHeight / doc.Height);
    }

    private static double MapPan(double sourceOffset, double sourceDisplay, double sourceViewport, double targetDisplay, double targetViewport)
    {
        var sourceTravel = sourceViewport - sourceDisplay;
        var targetTravel = targetViewport - targetDisplay;
        if (sourceTravel >= -0.5 || targetTravel >= -0.5) return (targetViewport - targetDisplay) / 2;
        var position = Math.Clamp(sourceOffset / sourceTravel, 0, 1);
        return position * targetTravel;
    }

    private async Task LoadPairAsync(int left, int right)
    {
        await LoadSideAsync(0, _files[left]);
        await LoadSideAsync(1, _files[right]);
    }

    private async Task LoadSideAsync(int side, string path)
    {
        if (_closed) return;
        var generation = ++_loadGeneration[side];
        _sideReady[side] = false;
        foreach (var button in _decisionButtons[side]) button.IsEnabled = false;
        _surfaces[side].SetDocument(null, ViewportFitMode.Contain);
        _dimensions[side].Text = T("正在读取图片…", "Loading image…");
        _decisions[side].Text = T("读取中", "Loading");
        _decisions[side].Foreground = BrushFrom("#A9B1B8");

        try
        {
            await _loadGates[side].WaitAsync();
            try
            {
                if (!IsCurrentLoad(side, path, generation)) return;
                var doc = await Task.Run(() => ImageLoader.Load(path));
                if (!IsCurrentLoad(side, path, generation)) return;
                _surfaces[side].SetDocument(doc, ViewportFitMode.Contain);
                _dimensions[side].Text = $"{doc.Width:N0} × {doc.Height:N0} px · {doc.Format}";
                _marks.TryAdd(path, Pending);
                _sideReady[side] = true;
                foreach (var button in _decisionButtons[side]) button.IsEnabled = true;
                RefreshDecision(side);
                if (_syncing) Synchronize(side);
            }
            finally { _loadGates[side].Release(); }
        }
        catch
        {
            if (!IsCurrentLoad(side, path, generation)) return;
            _surfaces[side].SetDocument(null, ViewportFitMode.Contain);
            _sideReady[side] = false;
            foreach (var button in _decisionButtons[side]) button.IsEnabled = false;
            _dimensions[side].Text = T("无法读取此图片，请选择其他文件。", "Could not read this image. Choose another file.");
            _decisions[side].Text = T("不可用", "Unavailable");
        }
    }

    private bool IsCurrentLoad(int side, string path, int generation) =>
        !_closed && generation == _loadGeneration[side] && _pickers[side].SelectedIndex >= 0 &&
        string.Equals(_files[_pickers[side].SelectedIndex], path, StringComparison.OrdinalIgnoreCase);

    private void SetMark(int side, string mark)
    {
        if (!_sideReady[side] || _pickers[side].SelectedIndex < 0) return;
        _marks[_files[_pickers[side].SelectedIndex]] = mark;
        RefreshDecision(0); RefreshDecision(1);
    }

    private void RefreshDecision(int side)
    {
        int index = _pickers[side].SelectedIndex;
        if (index < 0) return;
        var mark = _marks.GetValueOrDefault(_files[index], Pending);
        _decisions[side].Text = mark == Keep ? T("已标记保留", "Marked to keep") : T("待定", "Pending");
        _decisions[side].Foreground = mark == Keep ? BrushFrom("#91D9C5") : BrushFrom("#A9B1B8");
    }

    private void CycleBackground(Button button)
    {
        _backgroundIndex = (_backgroundIndex + 1) % _canvasBrushes.Length;
        foreach (var surface in _surfaces)
        {
            if (surface is null) continue;
            surface.CanvasBrush = _canvasBrushes[_backgroundIndex];
            surface.AlphaBackground = _backgroundIndex switch { 1 => "light", 2 => "checker", _ => "dark" };
            surface.InvalidateVisual();
        }
        button.Content = _backgroundNames[_backgroundIndex];
    }

    private async Task ExportCsvAsync()
    {
        var dialog = new SaveFileDialog { Title = T("导出本次审阅清单", "Export this review list"), Filter = "CSV (*.csv)|*.csv", DefaultExt = ".csv", AddExtension = true, FileName = $"YingView-review-{DateTime.Now:yyyyMMdd-HHmm}.csv" };
        if (dialog.ShowDialog(this) != true) return;
        var rows = _marks.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        var sb = new StringBuilder();
        sb.AppendLine(T("文件,标记", "File,Decision"));
        foreach (var (path, mark) in rows)
            sb.Append(Csv(path)).Append(',').AppendLine(Csv(mark == Keep ? T("保留", "Keep") : T("待定", "Pending")));
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            MessageBox.Show(this, T("清单已导出。原图片未作修改。", "The list was exported. Original images were not changed."), T("导出完成", "Export complete"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, T("导出失败：", "Export failed: ") + ex.Message, T("无法导出", "Could not export"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string Csv(string value)
    {
        var safe = value;
        var first = safe.TrimStart();
        if (first.Length > 0 && "=+-@\t\r\n".Contains(first[0])) safe = "'" + safe;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }

    private Button MakeButton(string text, bool primary = false, bool subtle = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(11, 6, 11, 6), Margin = new Thickness(3, 0, 0, 0), FontSize = 12, FontWeight = FontWeights.Medium, Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(1), BorderBrush = BrushFrom(primary ? "#557C72" : "#343D43"), Background = BrushFrom(primary ? "#263A35" : subtle ? "#20262B" : "#262C30"), Foreground = BrushFrom("#EDF0F2") };
        button.MouseEnter += (_, _) => button.Background = (Brush)FindResource("HoverBrush");
        button.MouseLeave += (_, _) => button.Background = BrushFrom(primary ? "#263A35" : subtle ? "#20262B" : "#262C30");
        button.PreviewMouseDown += (_, _) => button.Background = (Brush)FindResource("PressedBrush");
        button.PreviewMouseUp += (_, _) => button.Background = button.IsMouseOver ? (Brush)FindResource("HoverBrush") : BrushFrom(primary ? "#263A35" : subtle ? "#20262B" : "#262C30");
        return button;
    }

    private static Brush BrushFrom(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private string[] BuildDisplayNames()
    {
        var duplicates = _files.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _files.Select(path => duplicates.Contains(Path.GetFileName(path))
            ? $"{Path.GetFileName(path)}  ·  {Path.GetDirectoryName(path)}"
            : Path.GetFileName(path)).ToArray();
    }
    private static string SafeFullPath(string? path) { try { return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path); } catch { return ""; } }

    private static Brush CheckerBrush()
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            var light = new SolidColorBrush(Color.FromRgb(214, 217, 220));
            var dark = new SolidColorBrush(Color.FromRgb(174, 178, 182));
            dc.DrawRectangle(light, null, new Rect(0, 0, 24, 24));
            dc.DrawRectangle(dark, null, new Rect(0, 0, 12, 12));
            dc.DrawRectangle(dark, null, new Rect(12, 12, 12, 12));
        }
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 24, 24), Stretch = Stretch.None };
        brush.Freeze();
        return brush;
    }
}
