using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using YingView.Core;
using YingView.Imaging;

namespace YingView;

public partial class MainWindow : Window
{
    private readonly Preferences _settings = Preferences.Load();
    private readonly SemaphoreSlim _decodeGate = new(1);
    private readonly SemaphoreSlim _thumbnailGate = new(2);
    private readonly Dictionary<string, ImageDocument> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _cacheOrder = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _slideshow = new() { Interval = TimeSpan.FromSeconds(4) };
    private string[] _files = [];
    private string? _current;
    private int _index = -1;
    private int _generation;
    private int _openGeneration;
    private int _thumbGeneration;
    private bool _fullscreen;
    private bool _chromeHidden;
    private bool _loading;
    private WindowState _previousState;
    private string? _error;
    private string T(string zh, string en) => _settings.Language == "zh" ? zh : en;

    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme(_settings.Theme); ApplyLanguage();
        Surface.AlphaBackground = _settings.Background;
        Surface.ViewChanged += UpdateStatus;
        Surface.FullscreenRequested += ToggleFullscreen;
        _toastTimer.Tick += (_, _) => { Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop(); };
        _slideshow.Tick += async (_, _) => { if (!_loading && _files.Length > 1) await Navigate(1); };
        PreviewKeyDown += KeyPressed;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await OpenPaths(paths); e.Handled = true; };
        SourceInitialized += (_, _) => SetDarkTitleBar();
        Loaded += async (_, _) =>
        {
            WindowState = WindowState.Maximized;
            ApplyChrome();
            var paths = App.Arguments.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
            if (paths.Length > 0) await OpenPaths(paths);
            Surface.Focus();
        };
        Closing += (_, _) => { _slideshow.Stop(); _settings.Save(); };
    }

    private void ApplyLanguage()
    {
        Title = _current is null ? T("映窗 · YingView", "YingView") : Path.GetFileName(_current) + " · " + T("映窗", "YingView");
        OpenButton.Content = T("打开图片", "Open image");
        FitButton.Content = T("完整显示", "Fit"); FillButton.Content = T("铺满屏幕", "Fill");
        EmptyTitle.Text = T("打开，就是大画面", "Your images. A bigger view.");
        EmptySubtitle.Text = T("拖入图片或文件夹，小图也会自动放大", "Drop an image or folder. Even small images open large.");
        EmptyOpenButton.Content = T("选择图片", "Choose an image");
        InfoTitle.Text = T("图片信息", "Image information");
        Tip(OpenButton, T("打开图片", "Open images") + " · Ctrl+O");
        Tip(PreviousButton, T("上一张", "Previous image") + " · ←"); Tip(NextButton, T("下一张", "Next image") + " · →");
        Tip(FitButton, T("保持比例，完整放大显示", "Enlarge to fit without cropping") + " · F");
        Tip(FillButton, T("保持比例填满，可拖动查看边缘", "Fill the screen; drag to see cropped edges") + " · C");
        Tip(ActualButton, T("一个图片像素对应一个屏幕像素", "One image pixel per physical display pixel") + " · 1");
        Tip(RotateButton, T("向右旋转", "Rotate clockwise") + " · R");
        Tip(FullscreenButton, T("全屏 / 退出全屏", "Enter / leave fullscreen") + " · F11");
        Tip(ZoomInButton, T("放大", "Zoom in") + " · +"); Tip(ZoomOutButton, T("缩小", "Zoom out") + " · −");
        BuildMenu(); UpdateStatus();
        if (_error is not null) EmptyTitle.Text = T("这张图片暂时无法打开", "This image could not be opened");
    }
    private static void Tip(Control control, string text) { control.ToolTip = text; System.Windows.Automation.AutomationProperties.SetName(control, text); }
    private MenuItem Item(string title, Action action, bool? check = null)
    {
        var item = new MenuItem { Header = title };
        if (check.HasValue) { item.IsCheckable = true; item.IsChecked = check.Value; }
        item.Click += (_, _) => action(); return item;
    }
    private void BuildMenu()
    {
        MoreMenu.Items.Clear();
        MoreMenu.Items.Add(Item(T("打开文件夹", "Open folder") + "    Ctrl+Shift+O", OpenFolder));
        var recent = new MenuItem { Header = T("最近打开", "Recent images") };
        foreach (var path in _settings.RecentFiles.Take(8).ToArray())
            recent.Items.Add(Item(Path.GetFileName(path), async () => await OpenPaths([path])));
        if (recent.Items.Count > 0)
        {
            recent.Items.Add(new Separator());
            recent.Items.Add(Item(T("清空最近记录", "Clear recent history"), () => { _settings.RecentFiles.Clear(); SaveSettings(); BuildMenu(); }));
        }
        recent.IsEnabled = recent.Items.Count > 0; MoreMenu.Items.Add(recent);
        MoreMenu.Items.Add(new Separator());
        var background = new MenuItem { Header = T("透明图背景", "Transparency background") };
        foreach (var value in new[] { "dark", "light", "checker" })
        {
            var label = value switch { "light" => T("白色", "White"), "checker" => T("棋盘格", "Checkerboard"), _ => T("跟随主题", "Theme background") };
            background.Items.Add(Item(label, () => { _settings.Background = value; Surface.AlphaBackground = value; Surface.InvalidateVisual(); BuildMenu(); SaveSettings(); }, _settings.Background == value));
        }
        MoreMenu.Items.Add(background);
        var theme = new MenuItem { Header = T("界面风格", "Appearance") };
        foreach (var preset in ViewerTheme.BuiltIn)
            theme.Items.Add(Item(preset.Name, () => { ApplyTheme(preset); SaveSettings(); BuildMenu(); }, _settings.Theme.Name == preset.Name));
        theme.Items.Add(new Separator());
        theme.Items.Add(Item(T("导入主题…", "Import theme…"), ImportTheme));
        theme.Items.Add(Item(T("导出当前主题…", "Export current theme…"), ExportTheme)); MoreMenu.Items.Add(theme);
        var language = new MenuItem { Header = "语言 / Language" };
        language.Items.Add(Item("简体中文", () => SetLanguage("zh"), _settings.Language == "zh"));
        language.Items.Add(Item("English", () => SetLanguage("en"), _settings.Language == "en")); MoreMenu.Items.Add(language);
        MoreMenu.Items.Add(Item(T("缩略图栏", "Thumbnail strip") + "    T", ToggleFilmstrip, _settings.Filmstrip));
        MoreMenu.Items.Add(Item(T("图片信息", "Image information") + "    I", ToggleInfo, InfoPanel.Visibility == Visibility.Visible));
        MoreMenu.Items.Add(Item(T("镜像查看", "Mirror view") + "    H", () => Surface.Mirror(), Surface.Mirrored));
        MoreMenu.Items.Add(Item(T("幻灯片 · 每4秒", "Slideshow · every 4 seconds") + "    P", ToggleSlideshow, _slideshow.IsEnabled));
        MoreMenu.Items.Add(Item(T("暂停 / 播放动图", "Pause / play animation") + "    Space", () => Surface.PauseAnimation()));
        var defaultFit = new MenuItem { Header = T("打开时的显示方式", "Default image fit") };
        defaultFit.Items.Add(Item(T("完整显示，自动放大", "Enlarge to fit"), () => SetDefaultFit("contain"), _settings.DefaultFit == "contain"));
        defaultFit.Items.Add(Item(T("铺满屏幕", "Fill the screen"), () => SetDefaultFit("cover"), _settings.DefaultFit == "cover"));
        MoreMenu.Items.Add(defaultFit);
        MoreMenu.Items.Add(Item(T("打开图片后进入全屏", "Fullscreen when opening images"), () => { _settings.FullscreenOnOpen = !_settings.FullscreenOnOpen; SaveSettings(); BuildMenu(); }, _settings.FullscreenOnOpen));
        MoreMenu.Items.Add(new Separator());
        MoreMenu.Items.Add(Item(T("复制图片", "Copy image") + "    Ctrl+C", CopyImage));
        MoreMenu.Items.Add(Item(T("复制路径", "Copy file path") + "    Ctrl+Shift+C", CopyPath));
        MoreMenu.Items.Add(Item(T("在文件夹中显示", "Show in folder") + "    Ctrl+E", ShowInFolder));
        MoreMenu.Items.Add(Item(T("粘贴图片", "Paste image") + "    Ctrl+V", async () => await PasteImage()));
        MoreMenu.Items.Add(new Separator());
        MoreMenu.Items.Add(Item(T("快捷键", "Keyboard shortcuts") + "    F1", ShowHelp));
        MoreMenu.Items.Add(Item(T("许可信息", "Licenses"), ShowLicenses));
        MoreMenu.Items.Add(Item(T("关于映窗", "About YingView"), () => MessageBox.Show(this,
            T("映窗 · YingView 0.1.0\n由光之十一开发\n\n本地查看，无广告，无图片上传。\n源码：github.com/shixi-11/yingview", "YingView 0.1.0\nCreated by Shixi Lin\n\nLocal viewing. No ads. No image uploads.\nSource: github.com/shixi-11/yingview"), T("关于映窗", "About YingView"))));
    }
    private void ShowLicenses()
    {
        var assembly = typeof(MainWindow).Assembly;
        var content = new System.Text.StringBuilder();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("YingView.ThirdParty.") || n is "YingView.LICENSE" or "YingView.NOTICE").Order())
        {
            using var resource = assembly.GetManifestResourceStream(name);
            if (resource is null) continue;
            using var reader = new StreamReader(resource); content.AppendLine(name).AppendLine().AppendLine(reader.ReadToEnd()).AppendLine();
        }
        var text = new TextBox { Text = content.ToString(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (Brush)Application.Current.Resources["BarBrush"], Foreground = (Brush)Application.Current.Resources["TextBrush"], Padding = new Thickness(20), FontSize = 13, BorderThickness = new Thickness(0) };
        new Window { Owner = this, Title = T("许可信息", "Licenses"), Width = 760, Height = 580, MinWidth = 400, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = text }.ShowDialog();
    }
    private void SetLanguage(string language) { _settings.Language = language; ApplyLanguage(); SaveSettings(); }
    private void SetDefaultFit(string mode) { _settings.DefaultFit = mode; Surface.Fit(DefaultFit); SaveSettings(); BuildMenu(); }
    private ViewportFitMode DefaultFit => _settings.DefaultFit == "cover" ? ViewportFitMode.Cover : ViewportFitMode.Contain;
    private void SaveSettings() { if (!_settings.Save()) ShowToast(T("偏好设置暂时无法保存", "Preferences could not be saved")); }
    private void ApplyTheme(ViewerTheme theme)
    {
        theme.Validate(); _settings.Theme = theme;
        var resources = Application.Current.Resources;
        resources["ShellBrush"] = ViewerTheme.Brush(theme.Shell); resources["BarBrush"] = ViewerTheme.Brush(theme.Toolbar);
        resources["TextBrush"] = ViewerTheme.Brush(theme.Text); resources["MutedBrush"] = ViewerTheme.Brush(theme.Muted);
        resources["AccentBrush"] = ViewerTheme.Brush(theme.Accent);
        resources["LineBrush"] = ViewerTheme.Brush(theme.Name == "Paper" ? "#D8DCE0" : "#333A41");
        Surface.CanvasBrush = ViewerTheme.Brush(theme.Canvas); Surface.InvalidateVisual(); SetDarkTitleBar();
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private void SetDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle; if (hwnd == IntPtr.Zero) return;
        int dark = _settings.Theme.Name == "Paper" ? 0 : 1; _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }
    private void ImportTheme()
    {
        var dialog = new OpenFileDialog { Filter = "YingView theme (*.json)|*.json", Title = T("导入主题", "Import theme") };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 16_384) throw new InvalidDataException(T("主题文件过大", "Theme file is too large"));
            var theme = JsonSerializer.Deserialize<ViewerTheme>(File.ReadAllText(dialog.FileName), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException();
            theme.Validate(); ApplyTheme(theme); SaveSettings(); BuildMenu(); ShowToast(T("已应用主题：", "Theme applied: ") + theme.Name);
        }
        catch (Exception ex) { ShowToast(T("主题无法导入：", "Could not import theme: ") + ex.Message); }
    }
    private void ExportTheme()
    {
        var dialog = new SaveFileDialog { Filter = "YingView theme (*.json)|*.json", FileName = "YingView-theme.json", Title = T("导出主题", "Export theme") };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_settings.Theme, new JsonSerializerOptions { WriteIndented = true })); ShowToast(T("主题已导出", "Theme exported")); }
        catch (Exception ex) { ShowToast(T("无法导出：", "Could not export: ") + ex.Message); }
    }

    private async void OpenClick(object sender, RoutedEventArgs e)
    {
        var extensions = string.Join(';', ImageLoader.Extensions.Select(x => "*" + x));
        var dialog = new OpenFileDialog { Filter = T("图片", "Images") + $"|{extensions}|" + T("所有文件", "All files") + "|*.*", Multiselect = true, Title = T("打开图片", "Open images") };
        if (dialog.ShowDialog(this) == true) await OpenPaths(dialog.FileNames);
    }
    private async void OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = T("选择图片文件夹", "Choose an image folder") };
        if (dialog.ShowDialog(this) == true) await OpenPaths([dialog.FolderName]);
    }
    private async Task OpenPaths(string[] paths)
    {
        if (paths.Length == 0) return;
        var openGeneration = ++_openGeneration;
        try
        {
            var first = Path.GetFullPath(paths[0]);
            string[] files;
            string? select;
            if (Directory.Exists(first))
            {
                files = await Task.Run(() => Directory.EnumerateFiles(first).Where(ImageLoader.Supports).OrderBy(Path.GetFileName, NaturalStringComparer.Ordinal).ToArray());
                select = files.FirstOrDefault();
            }
            else if (paths.Length == 1 && ImageLoader.Supports(first))
            {
                files = await Task.Run(() => Directory.EnumerateFiles(Path.GetDirectoryName(first)!).Where(ImageLoader.Supports).OrderBy(Path.GetFileName, NaturalStringComparer.Ordinal).ToArray());
                select = first;
            }
            else
            {
                files = paths.Where(File.Exists).Where(ImageLoader.Supports).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Path.GetFileName, NaturalStringComparer.Ordinal).ToArray();
                select = files.FirstOrDefault();
            }
            if (openGeneration != _openGeneration) return;
            if (files.Length == 0 || select is null) { ShowToast(T("这里没有支持的图片", "No supported images found here")); return; }
            _files = files; _index = Array.FindIndex(files, x => string.Equals(x, select, StringComparison.OrdinalIgnoreCase));
            if (_index < 0) _index = 0;
            _slideshow.Stop(); await LoadCurrent();
            if (_settings.FullscreenOnOpen && !_fullscreen) ToggleFullscreen();
        }
        catch (Exception ex) { ShowToast(T("无法打开：", "Could not open: ") + ex.Message); }
    }
    private async Task Navigate(int direction)
    {
        if (_files.Length == 0) return;
        _index = (_index + direction + _files.Length) % _files.Length; await LoadCurrent();
    }
    private async Task LoadCurrent()
    {
        if (_index < 0 || _index >= _files.Length) return;
        var path = _files[_index]; var generation = ++_generation;
        _current = path; _loading = true; _error = null;
        Title = Path.GetFileName(path) + " · " + T("映窗", "YingView");
        UpdateStatus(); _ = BuildThumbnails();
        try
        {
            ImageDocument? doc;
            if (!_cache.TryGetValue(path, out doc))
            {
                doc = await Task.Run(async () =>
                {
                    await _decodeGate.WaitAsync();
                    try { return generation != _generation ? null : ImageLoader.Load(path); }
                    finally { _decodeGate.Release(); }
                });
                if (doc is null || generation != _generation) return;
                Cache(path, doc);
            }
            if (generation != _generation) return;
            Surface.SetDocument(doc, DefaultFit);
            EmptyPanel.Visibility = Visibility.Collapsed;
            _settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _settings.RecentFiles.Insert(0, path); _settings.RecentFiles = _settings.RecentFiles.Take(8).ToList();
            BuildMenu();
        }
        catch (Exception ex)
        {
            if (generation != _generation) return;
            _error = ex.Message; Surface.SetDocument(null, DefaultFit);
            EmptyPanel.Visibility = Visibility.Visible;
            EmptyTitle.Text = T("这张图片暂时无法打开", "This image could not be opened");
            EmptySubtitle.Text = T("可按方向键继续看下一张，或重新选择图片", "Use the arrow keys for another image, or choose a different file.");
            ShowToast(T("解码失败：", "Decode failed: ") + ex.Message);
        }
        finally
        {
            if (generation == _generation) { _loading = false; UpdateStatus(); Surface.Focus(); }
        }
    }
    private void Cache(string path, ImageDocument doc)
    {
        if (_cache.ContainsKey(path)) return;
        _cache[path] = doc; _cacheOrder.AddLast(path);
        while (_cache.Count > 3 || (_cache.Count > 1 && _cache.Values.Sum(v => v.EstimatedBytes) > 256L * 1024 * 1024))
        {
            var oldest = _cacheOrder.First!.Value; _cache.Remove(oldest); _cacheOrder.RemoveFirst();
        }
    }
    private async Task BuildThumbnails()
    {
        int generation = ++_thumbGeneration;
        Thumbnails.Children.Clear();
        if (!_settings.Filmstrip || _files.Length == 0) return;
        int start = Math.Clamp(_index - 4, 0, Math.Max(0, _files.Length - 9));
        var tasks = new List<Task>();
        for (int i = start; i < Math.Min(_files.Length, start + 9); i++)
        {
            var n = i; var path = _files[i];
            var image = new Image { Width = 72, Height = 56, Stretch = Stretch.Uniform, Margin = new Thickness(3) };
            var border = new Border { BorderBrush = i == _index ? (Brush)Application.Current.Resources["AccentBrush"] : Brushes.Transparent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(5), Child = image };
            var button = new Button { Content = border, Padding = new Thickness(2), ToolTip = Path.GetFileName(path) };
            System.Windows.Automation.AutomationProperties.SetName(button, Path.GetFileName(path));
            button.Click += async (_, _) => { _index = n; await LoadCurrent(); };
            Thumbnails.Children.Add(button);
            tasks.Add(LoadThumbnail(image, path, generation));
        }
        await Task.WhenAll(tasks);
    }
    private async Task LoadThumbnail(Image target, string path, int generation)
    {
        try
        {
            var bitmap = await Task.Run(async () =>
            {
                await _thumbnailGate.WaitAsync();
                try { return generation != _thumbGeneration ? null : ImageLoader.LoadThumbnail(path, 160); }
                finally { _thumbnailGate.Release(); }
            });
            if (generation == _thumbGeneration && bitmap is not null) target.Source = bitmap;
        }
        catch { /* A bad thumbnail never prevents the main image or navigation. */ }
    }
    private void UpdateStatus()
    {
        if (Surface is null || FileLabel is null) return;
        var doc = Surface.Document;
        FileLabel.Text = _current is null ? doc is null ? T("准备就绪 · 拖入图片开始", "Ready · Drop an image to begin") : T("剪贴板图片", "Clipboard image") : Path.GetFileName(_current);
        var counter = _files.Length > 0 ? $"{_index + 1} / {_files.Length}     " : "";
        DetailsLabel.Text = _loading ? T("正在打开…", "Opening…") : doc is null ? "" : counter + $"{doc.Width:N0} × {doc.Height:N0}     {doc.Format}" + (doc.Frames.Count > 1 ? $"     {doc.Frames.Count} " + T("帧", "frames") : "");
        ZoomLabel.Text = doc is null ? "—" : $"{Surface.View.Scale * Surface.View.DpiScale * 100:0.#}%";
        PreviousButton.IsEnabled = NextButton.IsEnabled = _files.Length > 1;
        foreach (var button in new[] { FitButton, FillButton, ActualButton, ZoomInButton, ZoomOutButton, RotateButton }) button.IsEnabled = doc is not null;
        if (_loading) InfoText.Text = T("正在打开…", "Opening…");
        else if (doc is not null)
        {
            long size = 0; try { if (_current is not null) size = new FileInfo(_current).Length; } catch { }
            InfoText.Text = (_current ?? T("剪贴板图片", "Clipboard image")) + $"\n\n{doc.Width:N0} × {doc.Height:N0} px\n{doc.Format}\n{size / 1024d / 1024d:0.##} MB\n" +
                T("显示比例：", "Display scale: ") + ZoomLabel.Text + "\n" + T("动画帧数：", "Animation frames: ") + doc.Frames.Count;
        }
        else InfoText.Text = _error ?? "";
    }
    private void ShowToast(string text) { ToastText.Text = text; Toast.Visibility = Visibility.Visible; _toastTimer.Stop(); _toastTimer.Start(); }
    private void ToggleFilmstrip() { _settings.Filmstrip = !_settings.Filmstrip; ApplyChrome(); BuildMenu(); if (_settings.Filmstrip) _ = BuildThumbnails(); SaveSettings(); }
    private void ToggleInfo() { InfoPanel.Visibility = InfoPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; BuildMenu(); }
    private void ToggleSlideshow()
    {
        if (_files.Length < 2) { ShowToast(T("打开包含多张图片的文件夹即可播放", "Open a folder with multiple images to start a slideshow")); return; }
        if (_slideshow.IsEnabled) _slideshow.Stop(); else _slideshow.Start(); BuildMenu();
        ShowToast(_slideshow.IsEnabled ? T("幻灯片已开始 · P 暂停", "Slideshow started · P to pause") : T("幻灯片已暂停", "Slideshow paused"));
    }
    private void ApplyChrome()
    {
        bool hidden = _fullscreen || _chromeHidden;
        Toolbar.Visibility = StatusBar.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        Filmstrip.Visibility = !hidden && _settings.Filmstrip ? Visibility.Visible : Visibility.Collapsed;
        Root.RowDefinitions[0].Height = new GridLength(hidden ? 0 : 60);
        Root.RowDefinitions[2].Height = new GridLength(!hidden && _settings.Filmstrip ? 90 : 0);
        Root.RowDefinitions[3].Height = new GridLength(hidden ? 0 : 30);
    }
    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _previousState = WindowState; WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized; _fullscreen = true;
            ShowToast(T("Esc 退出全屏 · 滚轮缩放 · 方向键翻图", "Esc to leave fullscreen · Scroll to zoom · Arrows to browse"));
        }
        else { WindowState = WindowState.Normal; WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize; WindowState = _previousState; _fullscreen = false; }
        ApplyChrome(); Surface.Focus();
    }
    private async void PreviousClick(object sender, RoutedEventArgs e) => await Navigate(-1);
    private async void NextClick(object sender, RoutedEventArgs e) => await Navigate(1);
    private void FitClick(object sender, RoutedEventArgs e) => Surface.Fit(ViewportFitMode.Contain);
    private void FillClick(object sender, RoutedEventArgs e) => Surface.Fit(ViewportFitMode.Cover);
    private void ActualClick(object sender, RoutedEventArgs e) => Surface.Fit(ViewportFitMode.Actual);
    private void ZoomInClick(object sender, RoutedEventArgs e) => Surface.Zoom(1.2);
    private void ZoomOutClick(object sender, RoutedEventArgs e) => Surface.Zoom(1 / 1.2);
    private void RotateClick(object sender, RoutedEventArgs e) => Surface.Rotate();
    private void FullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void CopyPath()
    {
        if (_current is null) return;
        try { Clipboard.SetText(_current); ShowToast(T("路径已复制", "File path copied")); } catch (Exception ex) { ShowToast(ex.Message); }
    }
    private void CopyImage()
    {
        if (Surface.Document is not { } doc) return;
        try
        {
            var data = new DataObject(); data.SetImage(doc.Frames[0]);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(doc.Frames[0]));
            using var png = new MemoryStream(); encoder.Save(png); data.SetData("PNG", new MemoryStream(png.ToArray())); Clipboard.SetDataObject(data, true);
            ShowToast(T("原图已复制，PNG数据保留透明度", "Original image copied, including PNG transparency data"));
        }
        catch (Exception ex) { ShowToast(T("剪贴板暂时不可用：", "Clipboard is unavailable: ") + ex.Message); }
    }
    private async Task PasteImage()
    {
        try
        {
            if (Clipboard.ContainsFileDropList()) { await OpenPaths(Clipboard.GetFileDropList().Cast<string>().ToArray()); return; }
            var bitmap = Clipboard.GetImage();
            if (bitmap is null) { ShowToast(T("剪贴板里没有图片", "No image on the clipboard")); return; }
            bitmap.Freeze(); ++_generation; ++_thumbGeneration; _files = []; _index = -1; _current = null; _slideshow.Stop();
            Surface.SetDocument(new ImageDocument([bitmap], [100], bitmap.PixelWidth, bitmap.PixelHeight, "CLIPBOARD", (long)bitmap.PixelWidth * bitmap.PixelHeight * 4), DefaultFit);
            EmptyPanel.Visibility = Visibility.Collapsed; Thumbnails.Children.Clear(); Title = T("剪贴板", "Clipboard") + " · YingView"; UpdateStatus();
        }
        catch (Exception ex) { ShowToast(ex.Message); }
    }
    private void ShowInFolder()
    {
        if (_current is null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", _current }, UseShellExecute = false }); }
        catch (Exception ex) { ShowToast(ex.Message); }
    }
    private void ShowHelp() => MessageBox.Show(this,
        T("Ctrl+O  打开图片\nCtrl+Shift+O  打开文件夹\n← / →  上一张 / 下一张\n滚轮  围绕鼠标缩放\n拖动  移动画面\nF / 0  完整显示并自动放大\nC  铺满屏幕\n1  原始像素\n+ / −  放大 / 缩小\nR  旋转    H  镜像\nB  切换透明背景\nT  缩略图    I  图片信息\nP  幻灯片    空格  暂停动图\nF11 / 双击  全屏\nTab  隐藏工具栏\nEsc  退出全屏 / 关闭信息\nCtrl+C  复制原图\nCtrl+Shift+C  复制路径\nCtrl+V  粘贴图片\nCtrl+E  在文件夹中显示\nF5  重新载入", "Ctrl+O  Open images\nCtrl+Shift+O  Open folder\n← / →  Previous / next image\nMouse wheel  Zoom around pointer\nDrag  Pan\nF / 0  Enlarge to fit\nC  Fill screen\n1  Actual pixels\n+ / −  Zoom in / out\nR  Rotate    H  Mirror\nB  Cycle transparency background\nT  Thumbnails    I  Image info\nP  Slideshow    Space  Pause animation\nF11 / Double-click  Fullscreen\nTab  Hide toolbars\nEsc  Leave fullscreen / close info\nCtrl+C  Copy original image\nCtrl+Shift+C  Copy file path\nCtrl+V  Paste image\nCtrl+E  Show in folder\nF5  Reload"), T("快捷键", "Keyboard shortcuts"));
    private async void KeyPressed(object sender, KeyEventArgs e)
    {
        if (MoreMenu.IsSubmenuOpen) return;
        if (e.OriginalSource is TextBox { IsReadOnly: false }) return;
        if (e.OriginalSource is TextBox text && text.SelectionLength > 0 && e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control); bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.O: if (shift) OpenFolder(); else OpenClick(this, e); break;
                case Key.C: if (shift) CopyPath(); else CopyImage(); break;
                case Key.V: await PasteImage(); break;
                case Key.E: ShowInFolder(); break;
                default: return;
            }
            e.Handled = true; return;
        }
        switch (e.Key)
        {
            case Key.Right: case Key.PageDown: await Navigate(1); break;
            case Key.Left: case Key.PageUp: await Navigate(-1); break;
            case Key.Home: if (_files.Length > 0) { _index = 0; await LoadCurrent(); } break;
            case Key.End: if (_files.Length > 0) { _index = _files.Length - 1; await LoadCurrent(); } break;
            case Key.F: case Key.D0: case Key.NumPad0: Surface.Fit(ViewportFitMode.Contain); break;
            case Key.C: Surface.Fit(ViewportFitMode.Cover); break;
            case Key.D1: case Key.NumPad1: Surface.Fit(ViewportFitMode.Actual); break;
            case Key.Add: case Key.OemPlus: Surface.Zoom(1.2); break;
            case Key.Subtract: case Key.OemMinus: Surface.Zoom(1 / 1.2); break;
            case Key.R: Surface.Rotate(); break;
            case Key.H: Surface.Mirror(); break;
            case Key.T: ToggleFilmstrip(); break;
            case Key.I: ToggleInfo(); break;
            case Key.P: ToggleSlideshow(); break;
            case Key.Space: Surface.PauseAnimation(); break;
            case Key.B:
                _settings.Background = _settings.Background switch { "dark" => "light", "light" => "checker", _ => "dark" };
                Surface.AlphaBackground = _settings.Background; Surface.InvalidateVisual(); BuildMenu(); SaveSettings(); break;
            case Key.F11: ToggleFullscreen(); break;
            case Key.Tab: _chromeHidden = !_chromeHidden; ApplyChrome(); break;
            case Key.Escape:
                if (_fullscreen) ToggleFullscreen(); else if (_chromeHidden) { _chromeHidden = false; ApplyChrome(); }
                else InfoPanel.Visibility = Visibility.Collapsed; _slideshow.Stop(); break;
            case Key.F1: ShowHelp(); break;
            case Key.F5: if (_current is not null) { _cache.Remove(_current); _cacheOrder.Remove(_current); await OpenPaths([_current]); } break;
            default: return;
        }
        e.Handled = true;
    }
}
