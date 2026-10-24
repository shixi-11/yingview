using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
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
using YingView.Files;
using YingView.Commerce;

namespace YingView;

public partial class MainWindow : Window
{
    private readonly Preferences _settings;
    private readonly Func<string, string[]> _readFolder;
    private readonly string[] _startupPaths;
    private Task? _startupOpen;
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
    private bool _deleting;
    private double _interfaceScale;
    private bool _compactToolbar;
    private StoreBillingService? _billing;
    private WindowState _previousState;
    private string? _error;
    private bool _currentFileMissing;
    private string T(string zh, string en) => _settings.Language == "zh" ? zh : en;
    internal bool IsChinese => _settings.Language == "zh";

    public MainWindow() : this(Preferences.Load(), App.Arguments) { }
    internal MainWindow(Preferences preferences, string[]? startupArguments = null, Func<string, string[]>? readFolder = null)
    {
        _settings = preferences;
        _readFolder = readFolder ?? ReadFolder;
        _startupPaths = (startupArguments ?? []).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        InitializeComponent();
        // Set the initial content before the window can render, including slow folder scans.
        _loading = _startupPaths.Length > 0;
        EmptyPanel.Visibility = _loading ? Visibility.Collapsed : Visibility.Visible;
        // This window only views images; text editing lives in separate dialogs.
        // Keep Chinese IMEs from consuming single-letter viewing shortcuts such as B/F/R.
        InputMethod.SetIsInputMethodEnabled(this, false);
        _interfaceScale = _settings.TextScale; _compactToolbar = _settings.CompactToolbar;
        ApplyTheme(_settings.Theme); ApplyLanguage(); ApplyInterfaceScale(_interfaceScale);
        ApplyCanvasBackground();
        Surface.ViewChanged += UpdateStatus;
        Surface.FullscreenRequested += ToggleFullscreen;
        _toastTimer.Tick += (_, _) => { Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop(); };
        _slideshow.Tick += async (_, _) => { if (!_loading && _files.Length > 1) await Navigate(1); };
        PreviewKeyDown += KeyPressed;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await OpenPaths(paths); e.Handled = true; };
        SourceInitialized += (_, _) => { SetDarkTitleBar(); InitializeBilling(); };
        SizeChanged += (_, _) => ApplyResponsiveToolbar();
        Loaded += async (_, _) =>
        {
            WindowState = WindowState.Maximized;
            ApplyChrome();
            await OpenStartupAsync();
            Surface.Focus();
        };
        Closing += (_, _) =>
        {
            _slideshow.Stop(); _toastTimer.Stop(); ++_generation; ++_thumbGeneration; ++_openGeneration;
            Surface.SetDocument(null, DefaultFit); _settings.Save();
        };
    }

    internal Task OpenStartupAsync() => _startupOpen ??= OpenPaths(_startupPaths);

    private void ApplyLanguage()
    {
        Title = _current is null ? ClientBranding.DisplayName(_settings.Language) : Path.GetFileName(_current) + " · " + ClientBranding.DisplayName(_settings.Language);
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/YingView;component/Assets/" + ClientBranding.IconResource(_settings.Language)));
        OpenButton.Content = T("打开图片", "Open image");
        FitButton.Content = T("完整显示", "Fit image");
        EmptyTitle.Text = T("每一张，都完整看见", "See the whole picture.");
        EmptySubtitle.Text = T("拖入图片或文件夹，自动按比例完整显示", "Drop an image or folder. It opens fully in view.");
        EmptyOpenButton.Content = T("选择图片", "Choose an image");
        InfoTitle.Text = T("图片信息", "Image information");
        Tip(OpenButton, T("打开图片", "Open images") + " · Ctrl+O");
        Tip(PreviousButton, T("上一张", "Previous image") + " · ←"); Tip(NextButton, T("下一张", "Next image") + " · →");
        Tip(FitButton, T("保持比例，完整放大显示", "Enlarge to fit without cropping") + " · F");
        Tip(ActualButton, T("一个图片像素对应一个屏幕像素", "One image pixel per physical display pixel") + " · 1");
        Tip(RotateButton, T("向右旋转", "Rotate clockwise") + " · R");
        Tip(CanvasToggleButton, T("切换黑白背景", "Switch black/white background") + " · B");
        Tip(FullscreenButton, T("全屏 / 退出全屏", "Enter / leave fullscreen") + " · F11");
        Tip(ZoomInButton, T("放大", "Zoom in") + " · +"); Tip(ZoomOutButton, T("缩小", "Zoom out") + " · −");
        Tip(StudioButton, T("自定义界面风格", "Customize appearance"));
        Tip(DeleteButton, T("移入回收站", "Move to Recycle Bin") + " · Delete / Del");
        Tip(FilmstripToggleButton, T("展开或收起缩略图", "Show or hide thumbnails") + " · T");
        ApplyChrome();
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
        var background = new MenuItem { Header = T("背景颜色", "Background color") };
        foreach (var value in new[] { "black", "white", "theme", "checker" })
        {
            var label = value switch { "black" => T("黑色", "Black"), "white" => T("白色", "White"), "checker" => T("棋盘格", "Checkerboard"), _ => T("跟随主题", "Theme background") };
            background.Items.Add(Item(label, () => SetCanvasBackground(value), _settings.Background == value || value == "white" && _settings.Background == "light" || value == "black" && _settings.Background == "dark"));
        }
        MoreMenu.Items.Add(background);
        var theme = new MenuItem { Header = T("界面风格", "Appearance") };
        foreach (var preset in ViewerTheme.BuiltIn)
            theme.Items.Add(Item(ViewerTheme.LocalizedName(preset.Name, IsChinese), () => { ApplyTheme(preset); SaveSettings(); BuildMenu(); }, _settings.Theme.Name == preset.Name));
        theme.Items.Add(new Separator());
        theme.Items.Add(Item(T("自定义风格…", "Customize appearance…"), OpenStyleStudio));
        theme.Items.Add(Item(T("导入主题…", "Import theme…"), ImportTheme));
        theme.Items.Add(Item(T("导出当前主题…", "Export current theme…"), ExportTheme)); MoreMenu.Items.Add(theme);
        if (ClientBranding.DistributionLanguage == "auto")
        {
            var language = new MenuItem { Header = T("语言", "Language") };
            language.Items.Add(Item("简体中文", () => SetLanguage("zh"), _settings.Language == "zh"));
            language.Items.Add(Item("English", () => SetLanguage("en"), _settings.Language == "en")); MoreMenu.Items.Add(language);
        }
        MoreMenu.Items.Add(Item(T("缩略图栏", "Thumbnail strip") + "    T", ToggleFilmstrip, _settings.Filmstrip));
        MoreMenu.Items.Add(Item(T("图片信息", "Image information") + "    I", ToggleInfo, InfoPanel.Visibility == Visibility.Visible));
        MoreMenu.Items.Add(Item(T("镜像查看", "Mirror view") + "    H", () => Surface.Mirror(), Surface.Mirrored));
        MoreMenu.Items.Add(Item(T("幻灯片 · 每4秒", "Slideshow · every 4 seconds") + "    P", ToggleSlideshow, _slideshow.IsEnabled));
        MoreMenu.Items.Add(Item(T("暂停 / 播放动图", "Pause / play animation") + "    Space", () => Surface.PauseAnimation()));
        MoreMenu.Items.Add(Item(T("打开图片后进入全屏", "Fullscreen when opening images"), () => { _settings.FullscreenOnOpen = !_settings.FullscreenOnOpen; SaveSettings(); BuildMenu(); }, _settings.FullscreenOnOpen));
        MoreMenu.Items.Add(new Separator());
        MoreMenu.Items.Add(Item(T("复制图片", "Copy image") + "    Ctrl+C", CopyImage));
        MoreMenu.Items.Add(Item(T("复制路径", "Copy file path") + "    Ctrl+Shift+C", CopyPath));
        MoreMenu.Items.Add(Item(T("在文件夹中显示", "Show in folder") + "    Ctrl+E", ShowInFolder));
        MoreMenu.Items.Add(Item(T("粘贴图片", "Paste image") + "    Ctrl+V", async () => await PasteImage()));
        var deleteItem = Item(T("移入回收站", "Move to Recycle Bin") + "    Delete", async () => await DeleteCurrent());
        deleteItem.IsEnabled = _current is not null && !_loading && !_deleting && !_currentFileMissing;
        MoreMenu.Items.Add(deleteItem);
        MoreMenu.Items.Add(new Separator());
        MoreMenu.Items.Add(Item(T("对照审阅…", "Compare & review…") + "    V", OpenReview));
        MoreMenu.Items.Add(Item(T("个人风格与专业版", "Personal styles & Pro"), ShowPlans));
        MoreMenu.Items.Add(Item(T("设为默认看图软件…", "Set as default image viewer…"), OpenDefaultSettings));
        MoreMenu.Items.Add(new Separator());
        MoreMenu.Items.Add(Item(T("快捷键", "Keyboard shortcuts") + "    F1", ShowHelp));
        MoreMenu.Items.Add(Item(T("许可信息", "Licenses"), ShowLicenses));
        MoreMenu.Items.Add(Item(T("关于映窗", "About YingView"), () => MessageBox.Show(this,
            T($"映窗 {DisplayVersion} 预览版\n由光之十一开发\n\n本地查看，无广告，无图片上传。\n源码：github.com/shixi-11/yingview", $"YingView {DisplayVersion} Preview\nCreated by Shixi Lin\n\nLocal viewing. No ads. No image uploads.\nSource: github.com/shixi-11/yingview"), T("关于映窗", "About YingView"))));
    }
    private void OpenDefaultSettings()
    {
        new DefaultAppsWindow(_settings.Language, startImmediately: true) { Owner = this }.ShowDialog();
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
    private void SetLanguage(string language) { _settings.Language = language; ApplyLanguage(); InitializeBilling(); SaveSettings(); }
    private void CanvasToggleClick(object sender, RoutedEventArgs e) => ToggleCanvasBackground();
    private void ToggleCanvasBackground()
    {
        var light = _settings.Background is "white" or "light"
            || _settings.Background == "theme" && Surface.CanvasBrush is SolidColorBrush brush
                && .2126 * brush.Color.R + .7152 * brush.Color.G + .0722 * brush.Color.B > 160;
        SetCanvasBackground(light ? "black" : "white");
    }
    private void SetCanvasBackground(string value)
    {
        _settings.Background = value; ApplyCanvasBackground(); BuildMenu(); SaveSettings(); Surface.Focus();
    }
    private void ApplyCanvasBackground()
    {
        Surface.CanvasMode = _settings.Background switch { "black" or "dark" => "black", "white" or "light" => "white", _ => "theme" };
        Surface.AlphaBackground = _settings.Background == "checker" ? "checker" : "dark";
        var foreground = Surface.CanvasMode == "white" ? Brushes.Black : Surface.CanvasMode == "black" ? Brushes.White : (Brush)Application.Current.Resources["TextBrush"];
        var muted = Surface.CanvasMode == "white" ? ViewerTheme.Brush("#555B61") : Surface.CanvasMode == "black" ? ViewerTheme.Brush("#C5CBD0") : (Brush)Application.Current.Resources["MutedBrush"];
        EmptyTitle.Foreground = foreground; EmptySubtitle.Foreground = muted;
        EmptyIcon.Foreground = muted; EmptyFormats.Foreground = muted;
        Surface.InvalidateVisual();
    }
    private static string DisplayVersion => typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(MainWindow).Assembly.GetName().Version?.ToString(3)
        ?? "";
    private ViewportFitMode DefaultFit => ViewportFitMode.Contain;
    private void SaveSettings() { if (!_settings.Save()) ShowToast(T("偏好设置暂时无法保存", "Preferences could not be saved")); }
    private void ApplyTheme(ViewerTheme theme)
    {
        theme.Validate(); _settings.Theme = theme;
        var resources = Application.Current.Resources;
        resources["ShellBrush"] = ViewerTheme.Brush(theme.Shell); resources["BarBrush"] = ViewerTheme.Brush(theme.Toolbar);
        resources["TextBrush"] = ViewerTheme.Brush(theme.Text); resources["MutedBrush"] = ViewerTheme.Brush(theme.Muted);
        resources["AccentBrush"] = ViewerTheme.Brush(theme.Accent);
        var color = (Color)ColorConverter.ConvertFromString(theme.Toolbar);
        bool light = .2126 * color.R + .7152 * color.G + .0722 * color.B > 160;
        resources["LineBrush"] = ViewerTheme.Brush(light ? "#D8DCE0" : "#333A41");
        resources["HoverBrush"] = ViewerTheme.Brush(light ? "#E2E8E6" : "#293138");
        resources["PressedBrush"] = ViewerTheme.Brush(light ? "#CFDAD6" : "#354139");
        var accent = (Color)ColorConverter.ConvertFromString(theme.Accent);
        resources["AccentTextBrush"] = ViewerTheme.Brush(.2126 * accent.R + .7152 * accent.G + .0722 * accent.B > 160 ? "#14231F" : "#FFFFFF");
        Surface.CanvasBrush = ViewerTheme.Brush(theme.Canvas); ApplyCanvasBackground(); SetDarkTitleBar();
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private void SetDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle; if (hwnd == IntPtr.Zero) return;
        var color = (Color)ColorConverter.ConvertFromString(_settings.Theme.Toolbar);
        int dark = .2126 * color.R + .7152 * color.G + .0722 * color.B > 160 ? 0 : 1; _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }
    private void ImportTheme()
    {
        var dialog = new OpenFileDialog { Filter = T("映窗主题 (*.json)|*.json", "YingView theme (*.json)|*.json"), Title = T("导入主题", "Import theme") };
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
        var dialog = new SaveFileDialog { Filter = T("映窗主题 (*.json)|*.json", "YingView theme (*.json)|*.json"), FileName = T("映窗主题.json", "YingView-theme.json"), Title = T("导出主题", "Export theme") };
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
        if (paths.Length == 0 || _deleting) return;
        var openGeneration = ++_openGeneration;
        ++_generation; // A slow decode for the previous selection must not overwrite this open request.
        _loading = true;
        EmptyPanel.Visibility = Visibility.Collapsed;
        UpdateStatus();
        try
        {
            var first = Path.GetFullPath(paths[0]);
            string[] files;
            string? select;
            if (Directory.Exists(first))
            {
                files = await Task.Run(() => _readFolder(first));
                select = files.FirstOrDefault();
            }
            else if (paths.Length == 1 && ImageLoader.Supports(first))
            {
                await OpenSingleImage(first, openGeneration);
                return;
            }
            else
            {
                files = paths.Where(File.Exists).Where(ImageLoader.Supports).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Path.GetFileName, NaturalStringComparer.Ordinal).ToArray();
                select = files.FirstOrDefault();
            }
            if (openGeneration != _openGeneration) return;
            if (files.Length == 0)
            {
                ShowEmptyState(T("没有找到支持的图片", "No supported images found"), T("请选择其他文件夹，或拖入图片。", "Choose another folder or drop an image here."));
                return;
            }
            if (select is null)
            {
                ShowEmptyState(T("图片已不存在", "Image no longer exists"), T("文件可能已被移动或删除。请打开所在文件夹，或重新选择图片。", "The file may have been moved or deleted. Open its folder or choose another image."));
                return;
            }
            _files = files; _index = Array.FindIndex(files, x => string.Equals(x, select, StringComparison.OrdinalIgnoreCase));
            if (_index < 0) _index = 0;
            _slideshow.Stop(); await LoadCurrent();
            if (openGeneration == _openGeneration && _settings.FullscreenOnOpen && !_fullscreen) ToggleFullscreen();
        }
        catch
        {
            if (openGeneration != _openGeneration) return;
            ShowEmptyState(T("无法打开此位置", "Could not open this location"), T("请检查文件或文件夹是否仍存在，以及访问权限。", "Check that the file or folder still exists and that you have access."));
            ShowToast(T("无法打开，请检查文件或文件夹是否仍存在，以及访问权限。", "Could not open it. Check that the file or folder still exists and that you have access."));
        }
    }
    private static string[] ReadFolder(string path) => Directory.EnumerateFiles(path).Where(ImageLoader.Supports)
        .OrderBy(Path.GetFileName, NaturalStringComparer.Ordinal).ToArray();

    private async Task OpenSingleImage(string path, int openGeneration)
    {
        if (!File.Exists(path))
        {
            ShowEmptyState(T("图片已不存在", "Image no longer exists"), T("文件可能已被移动或删除。请打开所在文件夹，或重新选择图片。", "The file may have been moved or deleted. Open its folder or choose another image."));
            return;
        }
        // The requested image is usable before a large or slow folder has finished indexing.
        _files = [path]; _index = 0; _slideshow.Stop();
        await LoadCurrent();
        if (openGeneration != _openGeneration) return;
        if (_settings.FullscreenOnOpen && !_fullscreen) ToggleFullscreen();
        try
        {
            var files = await Task.Run(() => _readFolder(Path.GetDirectoryName(path)!));
            if (openGeneration != _openGeneration || !string.Equals(_current, path, StringComparison.OrdinalIgnoreCase)) return;
            var index = Array.FindIndex(files, file => string.Equals(file, path, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return; // The file may have moved since its successful decode.
            _files = files; _index = index; UpdateStatus(); _ = BuildThumbnails();
        }
        catch { /* A folder access failure must not hide an independently readable image. */ }
    }
    private void ShowEmptyState(string title, string subtitle)
    {
        Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop();
        ++_generation; ++_thumbGeneration;
        _files = []; _index = -1; _current = null; _loading = false; _currentFileMissing = false; _error = null;
        _slideshow.Stop(); Surface.SetDocument(null, DefaultFit); Thumbnails.Children.Clear();
        EmptyPanel.Visibility = Visibility.Visible; EmptyTitle.Text = title; EmptySubtitle.Text = subtitle;
        Title = ClientBranding.DisplayName(_settings.Language); BuildMenu(); UpdateStatus(); Surface.Focus();
    }
    private async Task Navigate(int direction)
    {
        if (_files.Length == 0 || _deleting) return;
        _index = (_index + direction + _files.Length) % _files.Length; await LoadCurrent();
    }
    private async Task LoadCurrent()
    {
        if (_index < 0 || _index >= _files.Length) return;
        Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop();
        var path = _files[_index]; var generation = ++_generation;
        _current = path; _loading = true; _error = null;
        EmptyPanel.Visibility = Visibility.Collapsed;
        _currentFileMissing = false;
        Title = Path.GetFileName(path) + " · " + T("映窗", "YingView");
        UpdateStatus();
        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Image file no longer exists.", path);
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
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _ = BuildThumbnails()));
            _settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _settings.RecentFiles.Insert(0, path); _settings.RecentFiles = _settings.RecentFiles.Take(8).ToList();
            BuildMenu();
        }
        catch (Exception)
        {
            if (generation != _generation) return;
            _error = T("图片无法打开，可能格式不受支持或文件已损坏。", "This image could not be opened. Its format may be unsupported or the file may be damaged."); Surface.SetDocument(null, DefaultFit);
            _currentFileMissing = !File.Exists(path);
            EmptyPanel.Visibility = Visibility.Visible;
            EmptyTitle.Text = T("这张图片暂时无法打开", "This image could not be opened");
            EmptySubtitle.Text = _currentFileMissing
                ? T("文件可能已被移动或删除。按 F5 重新读取当前文件夹。", "The file may have been moved or deleted. Press F5 to reload the folder.")
                : T("可按方向键继续看下一张，或重新选择图片", "Use the arrow keys for another image, or choose a different file.");
            ShowToast(_error);
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
            var image = new Image { Width = 58, Height = 40, Stretch = Stretch.Uniform, Margin = new Thickness(2) };
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
        PreviousButton.IsEnabled = NextButton.IsEnabled = _files.Length > 1 && !_deleting;
        foreach (var button in new[] { FitButton, ActualButton, ZoomInButton, ZoomOutButton, RotateButton }) button.IsEnabled = doc is not null;
        DeleteButton.IsEnabled = _current is not null && !_loading && !_deleting && !_currentFileMissing;
        FitButton.Background = doc is not null && !Surface.View.HasManualZoom && Surface.View.FitMode == ViewportFitMode.Contain ? (Brush)Application.Current.Resources["HoverBrush"] : Brushes.Transparent;
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
        Root.RowDefinitions[0].Height = new GridLength(hidden ? 0 : (_compactToolbar ? 40 : 48) * Math.Max(1, _interfaceScale));
        Root.RowDefinitions[2].Height = new GridLength(!hidden && _settings.Filmstrip ? 62 : 0);
        Root.RowDefinitions[3].Height = new GridLength(hidden ? 0 : 28 * Math.Max(1, _interfaceScale));
        FilmstripLabel.Text = _settings.Filmstrip ? T("收起缩略图", "Hide thumbnails") : T("缩略图", "Thumbnails");
        FilmstripChevron.Text = _settings.Filmstrip ? "\uE70D" : "\uE70E";
        ApplyResponsiveToolbar();
    }
    private void ApplyResponsiveToolbar()
    {
        if (ZoomLabel is null) return;
        var showZoom = ActualWidth > 900 * Math.Max(1, _interfaceScale);
        ZoomLabel.Visibility = ZoomInButton.Visibility = ZoomOutButton.Visibility = showZoom ? Visibility.Visible : Visibility.Collapsed;
        DetailsLabel.Visibility = ActualWidth > 860 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ApplyInterfaceScale(double scale)
    {
        _interfaceScale = Math.Clamp(scale, .85, 1.3);
        Application.Current.Resources["ControlTextSize"] = 13 * _interfaceScale;
        FileLabel.FontSize = DetailsLabel.FontSize = ZoomLabel.FontSize = FilmstripToggleButton.FontSize = 12 * _interfaceScale;
        InfoText.FontSize = ToastText.FontSize = EmptyOpenButton.FontSize = 13 * _interfaceScale;
        InfoTitle.FontSize = 15 * _interfaceScale;
        EmptyTitle.FontSize = 23 * _interfaceScale; EmptySubtitle.FontSize = 14 * _interfaceScale;
        ApplyChrome();
    }
    private void ApplyCompactToolbar(bool compact) { _compactToolbar = compact; ApplyChrome(); }
    private void FilmstripClick(object sender, RoutedEventArgs e) => ToggleFilmstrip();
    private void StyleStudioClick(object sender, RoutedEventArgs e) => OpenStyleStudio();
    private void OpenStyleStudio()
    {
        var studio = new StyleStudioWindow(_settings, ApplyTheme, ApplyInterfaceScale, ApplyCompactToolbar, _billing?.Snapshot.HasPro == true) { Owner = this };
        studio.ShowDialog();
        ApplyTheme(_settings.Theme); ApplyChrome(); BuildMenu();
        if (studio.ProRequested) ShowPlans();
    }
    private void InitializeBilling()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        _billing = new StoreBillingService(hwnd, _settings.Language == "zh");
        _ = _billing.LoadAsync();
    }
    private void ShowPlans()
    {
        if (_billing is null) InitializeBilling();
        if (_billing is null) return;
        new PlansWindow(_billing, _settings.Language == "zh") { Owner = this }.ShowDialog();
    }
    private void OpenReview()
    {
        if (_current is null || _files.Length < 2)
        {
            ShowToast(T("打开至少两张图片后，即可对照审阅", "Open at least two images to compare and review")); return;
        }
        _slideshow.Stop();
        new Review.ReviewWindow(_files, _current, _settings.Language == "zh") { Owner = this }.ShowDialog();
    }
    private async void DeleteClick(object sender, RoutedEventArgs e) => await DeleteCurrent();
    private async Task DeleteCurrent()
    {
        if (_current is null || _loading || _deleting) return;
        var path = _current;
        _deleting = true; _slideshow.Stop(); UpdateStatus();
        try
        {
            if (!await RecycleBinService.RecycleAsync(path, new WindowInteropHelper(this).Handle))
            {
                ShowToast(T("未能移入回收站，图片已保留。", "Could not move this image to the Recycle Bin. The image was kept.")); return;
            }
            _cache.Remove(path); _cacheOrder.Remove(path);
            _settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            int removedIndex = Array.FindIndex(_files, p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _files = _files.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (string.Equals(_current, path, StringComparison.OrdinalIgnoreCase))
            {
                if (_files.Length > 0)
                {
                    _index = Math.Clamp(removedIndex, 0, _files.Length - 1); await LoadCurrent();
                }
                else
                {
                    ++_generation; ++_thumbGeneration; _index = -1; _current = null; _error = null;
                    Surface.SetDocument(null, DefaultFit); Thumbnails.Children.Clear();
                    EmptyPanel.Visibility = Visibility.Visible; ApplyLanguage();
                }
            }
            else _index = Array.FindIndex(_files, p => string.Equals(p, _current, StringComparison.OrdinalIgnoreCase));
            BuildMenu(); SaveSettings();
            ShowToast(T("已移入回收站，可在回收站恢复", "Moved to Recycle Bin. You can restore it there."));
        }
        catch (NotSupportedException) { ShowToast(T("这个位置不支持回收站，图片已保留。请在文件夹中处理。", "This location does not support the Recycle Bin. The image was kept. Manage it in its folder.")); }
        catch (FileNotFoundException) { ShowToast(T("图片已被移动或删除，按 F5 刷新文件夹", "This image has been moved or deleted. Press F5 to refresh.")); }
        catch { ShowToast(T("未能移入回收站，图片已保留。请检查文件权限或是否正被占用。", "Could not move this image to the Recycle Bin. Check permissions or whether another app is using it.")); }
        finally { _deleting = false; UpdateStatus(); BuildMenu(); Surface.Focus(); }
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
        if (_deleting) return;
        try
        {
            if (Clipboard.ContainsFileDropList()) { await OpenPaths(Clipboard.GetFileDropList().Cast<string>().ToArray()); return; }
            var bitmap = Clipboard.GetImage();
            if (bitmap is null) { ShowToast(T("剪贴板里没有图片", "No image on the clipboard")); return; }
            bitmap.Freeze(); ++_generation; ++_thumbGeneration; ++_openGeneration; _files = []; _index = -1; _current = null; _loading = false; _error = null; _slideshow.Stop();
            Surface.SetDocument(new ImageDocument([bitmap], [100], bitmap.PixelWidth, bitmap.PixelHeight, "CLIPBOARD", (long)bitmap.PixelWidth * bitmap.PixelHeight * 4), DefaultFit);
            EmptyPanel.Visibility = Visibility.Collapsed; Thumbnails.Children.Clear(); Title = T("剪贴板", "Clipboard") + " · " + ClientBranding.DisplayName(_settings.Language); UpdateStatus(); BuildMenu();
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
        T("Ctrl+O  打开图片\nCtrl+Shift+O  打开文件夹\n← / →  上一张 / 下一张\n滚轮  围绕鼠标缩放\n拖动  移动画面\nF / 0  完整显示并自动放大\n1  原始像素\n+ / −  放大 / 缩小\nR  旋转    H  镜像\nB  切换黑白背景\nDelete / Del  移入回收站\nV  对照审阅\nT  缩略图    I  图片信息\nP  幻灯片    空格  暂停动图\nF11 / 双击  全屏\nTab  隐藏工具栏\nEsc  退出全屏 / 关闭信息\nCtrl+C  复制原图\nCtrl+Shift+C  复制路径\nCtrl+V  粘贴图片\nCtrl+E  在文件夹中显示\nF5  重新载入", "Ctrl+O  Open images\nCtrl+Shift+O  Open folder\n← / →  Previous / next image\nMouse wheel  Zoom around pointer\nDrag  Pan\nF / 0  Enlarge to fit\n1  Actual pixels\n+ / −  Zoom in / out\nR  Rotate    H  Mirror\nB  Switch black/white background\nDelete / Del  Move to Recycle Bin\nV  Compare & review\nT  Thumbnails    I  Image info\nP  Slideshow    Space  Pause animation\nF11 / Double-click  Fullscreen\nTab  Hide toolbars\nEsc  Leave fullscreen / close info\nCtrl+C  Copy original image\nCtrl+Shift+C  Copy file path\nCtrl+V  Paste image\nCtrl+E  Show in folder\nF5  Reload"), T("快捷键", "Keyboard shortcuts"));
    private async void KeyPressed(object sender, KeyEventArgs e)
    {
        // Preserve native keyboard handling and prevent accidental deletion while
        // focus is on toolbar/menu controls rather than the image canvas.
        if (MoreMenu.IsSubmenuOpen || Keyboard.FocusedElement is TextBox || e.OriginalSource is TextBox) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;
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
        if ((Keyboard.FocusedElement is Control and not Window || e.OriginalSource is Control and not Window)
            && e.Key is not (Key.Escape or Key.F11 or Key.F1 or Key.F5)) return;
        switch (e.Key)
        {
            case Key.Right: case Key.PageDown: await Navigate(1); break;
            case Key.Left: case Key.PageUp: await Navigate(-1); break;
            case Key.Home: if (_files.Length > 0) { _index = 0; await LoadCurrent(); } break;
            case Key.End: if (_files.Length > 0) { _index = _files.Length - 1; await LoadCurrent(); } break;
            case Key.F: case Key.D0: case Key.NumPad0: Surface.Fit(ViewportFitMode.Contain); break;
            case Key.Delete: if (!e.IsRepeat) await DeleteCurrent(); break;
            case Key.V: OpenReview(); break;
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
                ToggleCanvasBackground(); break;
            case Key.F11: ToggleFullscreen(); break;
            case Key.Tab: _chromeHidden = !_chromeHidden; ApplyChrome(); break;
            case Key.Escape:
                if (_fullscreen) ToggleFullscreen(); else if (_chromeHidden) { _chromeHidden = false; ApplyChrome(); }
                else InfoPanel.Visibility = Visibility.Collapsed; _slideshow.Stop(); break;
            case Key.F1: ShowHelp(); break;
            case Key.F5: await ReloadCurrent(); break;
            default: return;
        }
        e.Handled = true;
    }
    private async Task ReloadCurrent()
    {
        if (_current is null || _deleting) return;
        var path = _current;
        _cache.Remove(path); _cacheOrder.Remove(path);
        var directory = Path.GetDirectoryName(path);
        await OpenPaths([File.Exists(path) || !Directory.Exists(directory) ? path : directory!]);
    }
}
