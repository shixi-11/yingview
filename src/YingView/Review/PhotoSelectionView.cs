using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using YingView.Core;
using YingView.Files;
using YingView.Imaging;

namespace YingView.Review;

public sealed class PhotoSelectionView : UserControl, IDisposable
{
    private readonly Preferences _preferences;
    private readonly ComboBox _pageSizeChoice = new() { Width = 70, Margin = new Thickness(6, 0, 14, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Slider _thumbnailSize = new() { Minimum = 120, Maximum = 320, Width = 132, Margin = new Thickness(8, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
    private readonly TextBlock _selectedCount = new() { Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 4) };
    private int PageSize => (int)(_pageSizeChoice.SelectedItem ?? Preferences.DefaultPhotoPageSize);
    private string? _activePath;
    private readonly bool _zh;
    private readonly List<string> _files;
    private readonly PhotoSelection _selection = new();
    private readonly SemaphoreSlim _thumbnailGate = new(3);
    private readonly PhotoSelectionStore _store;
    private readonly Func<IEnumerable<string>, IntPtr, Task<RecycleBinService.BatchResult>> _recycle;
    private readonly ObservableCollection<Row> _rows = [];
    private readonly ListBox _grid = new() { SelectionMode = SelectionMode.Extended, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly CheckBox _onlyFavorites = new() { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
    private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White, Margin = new Thickness(8, 0, 12, 4) };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _compare, _preview, _favorite, _unfavorite, _trash, _export, _previous, _next;
    private HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _moved = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _recycled = new(StringComparer.OrdinalIgnoreCase);
    private bool _updating, _busy, _closed;
    private readonly Grid _pages = new();
    private readonly Grid _gridPage;
    private FrameworkElement? _detail;
    internal event Action? BackRequested;
    internal bool IsBusy => _busy;
    private int _page, _generation;
    private const string ListKey = "favorites";
    private readonly TextBlock _folder = new() { TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
    private readonly TextBlock _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 18, Foreground = Brushes.LightGray, TextAlignment = TextAlignment.Center };
    private readonly Button _openFolder, _clearImport;
    public bool ScopeChanged { get; private set; }
    public IReadOnlyList<string> CurrentFiles => _files;
    public IReadOnlyCollection<string> RecycledFiles => _recycled;
    public IReadOnlyCollection<string> MovedFiles => _moved;
    private string T(string zh, string en) => _zh ? zh : en;
    private string[] Filtered => _files.Where(p => _onlyFavorites.IsChecked != true || _favorites.Contains(p)).ToArray();

    public PhotoSelectionView(string[] files, string current, bool chinese, PhotoSelectionStore? store = null,
        Func<IEnumerable<string>, IntPtr, Task<RecycleBinService.BatchResult>>? recycle = null, Preferences? preferences = null)
    {
        _preferences = preferences ?? Preferences.Load();
        _pageSizeChoice.ItemsSource = Preferences.PhotoPageSizes;
        _pageSizeChoice.SelectedItem = Preferences.PhotoPageSizes.Contains(_preferences.PhotoPageSize) ? _preferences.PhotoPageSize : Preferences.DefaultPhotoPageSize;
        _thumbnailSize.Value = double.IsFinite(_preferences.PhotoThumbnailSize) ? Math.Clamp(_preferences.PhotoThumbnailSize, 120, 320) : 180;
        _activePath = current;
        _zh = chinese; _store = store ?? new(); _recycle = recycle ?? RecycleBinService.RecycleManyAsync;
        _files = files.Where(ImageLoader.Supports).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Focusable = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Background = ViewerTheme.Brush("#111417"); Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 13;
        Resources["BarBrush"] = ViewerTheme.Brush("#1B2025"); Resources["LineBrush"] = ViewerTheme.Brush("#333A41");
        Resources["TextBrush"] = Brushes.White; Resources["MutedBrush"] = Brushes.LightGray;
        Resources["HoverBrush"] = ViewerTheme.Brush("#303A3E"); Resources["PressedBrush"] = ViewerTheme.Brush("#202824");
        var root = _gridPage = new Grid { Margin = new Thickness(16), Background = Background };
        TextElement.SetForeground(root, Brushes.White);
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel();
        var title = new DockPanel();
        var back = Action(T("返回看图", "Back to viewer"), () => { if (!_busy) BackRequested?.Invoke(); });
        DockPanel.SetDock(back, Dock.Right);
        title.Children.Add(back);
        title.Children.Add(new TextBlock { Text = T("选片", "Photo selection"), FontSize = 22, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(title);
        var options = new DockPanel { Margin = new Thickness(0, 12, 0, 10) };
        _openFolder = Action(T("打开文件夹…", "Open folder…"), OpenFolder);
        _openFolder.Background = ViewerTheme.Brush("#2B3944"); _openFolder.Foreground = Brushes.White;
        DockPanel.SetDock(_openFolder, Dock.Left); options.Children.Add(_openFolder);
        _clearImport = Action(T("清空导入", "Clear imported"), ClearImported);
        _clearImport.ToolTip = T("清空当前照片列表，保留文件和收藏", "Clear this photo list; keep files and favorites");
        DockPanel.SetDock(_clearImport, Dock.Left); options.Children.Add(_clearImport);
        _onlyFavorites.Content = T("只看收藏", "Favorites only"); DockPanel.SetDock(_onlyFavorites, Dock.Right); options.Children.Add(_onlyFavorites);
        options.Children.Add(_folder); heading.Children.Add(options); root.Children.Add(heading);
        _empty.Text = T("打开或拖入文件夹，开始选片", "Open or drop a folder to start selecting photos");
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        tools.Children.Add(Action(T("全选", "Select all"), () => { _selection.Replace(Filtered); ApplySelection(); }));
        tools.Children.Add(Action(T("取消选中", "Deselect all"), () => { _selection.Replace([]); ApplySelection(); }));
        _favorite = Action(T("收藏所选", "Favorite"), () => SaveFavorites(_selection.Selected, true)); tools.Children.Add(_favorite);
        _unfavorite = Action(T("取消收藏", "Unfavorite"), () => SaveFavorites(_selection.Selected, false)); tools.Children.Add(_unfavorite);
        _compare = Action(T("对比两张", "Compare two"), CompareSelected); tools.Children.Add(_compare);
        _preview = Action(T("查看原图", "View original"), PreviewSelected); tools.Children.Add(_preview);
        _trash = Action(T("移入回收站", "Move to Recycle Bin"), async () => await DeleteSelectedAsync()); tools.Children.Add(_trash);
        _export = Action(T("导出收藏清单…", "Export favorites…"), ExportFavorites); tools.Children.Add(_export);
        var selectAll = tools.Children[0]; var deselect = tools.Children[1]; tools.Children.Clear();
        _compare.Background = ViewerTheme.Brush("#264A42"); _compare.Foreground = Brushes.White;
        _preview.Background = ViewerTheme.Brush("#27323C"); _preview.Foreground = Brushes.White;
        foreach (UIElement item in new[] { (UIElement)_preview, _compare, selectAll, deselect, _favorite, _unfavorite, _trash, _export }) tools.Children.Add(item);
        _trash.Margin = new Thickness(16, 0, 5, 4);
        tools.Children.Add(_selectedCount);
        Grid.SetRow(tools, 1); root.Children.Add(tools);
        ScrollViewer.SetHorizontalScrollBarVisibility(_grid, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_grid, ScrollBarVisibility.Auto);
        _grid.ItemsSource = _rows;
        _grid.ItemsPanel = (ItemsPanelTemplate)XamlReader.Parse("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><WrapPanel/></ItemsPanelTemplate>");
        _grid.ItemTemplate = (DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Width='{Binding CardWidth}'><Border Height='{Binding PreviewHeight}' Background='#090B0D' CornerRadius='5'><Image Source='{Binding Thumbnail}' Stretch='Uniform' Margin='3'/></Border><TextBlock Text='{Binding Name}' ToolTip='{Binding Name}' Foreground='#EDF0F2' TextTrimming='CharacterEllipsis' Margin='2,7,2,4'/><CheckBox Content='{Binding FavoriteLabel}' IsChecked='{Binding Favorite,Mode=TwoWay}' Foreground='#91D9C5' Margin='2,0,2,3'/></StackPanel></DataTemplate>");
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(3))); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, ViewerTheme.Brush("#1B2025"))); style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ListBoxItem'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}' CornerRadius='7' SnapsToDevicePixels='True'><ContentPresenter/></Border></ControlTemplate>")));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, ViewerTheme.Brush("#91D9C5"))); selected.Setters.Add(new Setter(Control.BackgroundProperty, ViewerTheme.Brush("#283832"))); style.Triggers.Add(selected);
        _grid.ItemContainerStyle = style;
        _grid.PreviewMouseLeftButtonDown += GridMouseDown;
        _grid.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (_busy || e.OriginalSource is not DependencyObject source || ItemsControl.ContainerFromElement(_grid, source) is not ListBoxItem { Content: Row row }) return;
            _activePath = row.Path;
            if (!_selection.Selected.Contains(row.Path)) { _selection.Replace([row.Path]); ApplySelection(); }
            e.Handled = true; // Keep an existing multi-selection when right-clicking one of its photos.
        };
        var context = new ContextMenu();
        MenuItem MenuAction(string title, Action action)
        {
            var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); context.Items.Add(item); return item;
        }
        var menuPreview = MenuAction(T("查看原图", "View original"), PreviewSelected);
        var menuCompare = MenuAction(T("对比两张", "Compare two"), CompareSelected);
        context.Items.Add(new Separator());
        var menuRemove = MenuAction(T("移出本次选片", "Remove from this selection"), RemoveSelectedFromScope);
        var menuMove = MenuAction(T("移动到文件夹…", "Move to folder…"), ChooseMoveFolder);
        context.Items.Add(new Separator());
        var menuTrash = MenuAction(T("将所选移入回收站…", "Move selected to Recycle Bin…"), async () => await DeleteSelectedAsync());
        context.Opened += (_, _) =>
        {
            menuPreview.IsEnabled = _preview.IsEnabled; menuCompare.IsEnabled = _compare.IsEnabled;
            menuTrash.IsEnabled = menuRemove.IsEnabled = menuMove.IsEnabled = !_busy && _selection.Selected.Count > 0;
        };
        _grid.ContextMenu = context;
        _grid.SelectionChanged += (_, _) => { if (!_updating) { var visibleSelection = _grid.SelectedItems.Cast<Row>().Select(r => r.Path).ToArray(); var chosen = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? _selection.Selected.Except(_rows.Select(r => r.Path), StringComparer.OrdinalIgnoreCase).Concat(visibleSelection).ToArray() : visibleSelection; _selection.Replace(chosen); if (visibleSelection.Length == 1) _activePath = visibleSelection[0]; UpdateActions(); } };
        var content = new Grid(); content.Children.Add(_grid); content.Children.Add(_empty);
        Grid.SetRow(content, 2); root.Children.Add(content);
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var paging = new WrapPanel();
        _previous = Action(T("← 上一页", "← Previous page"), () => TurnPage(-1));
        _next = Action(T("下一页 →", "Next page →"), () => TurnPage(1));
        _previous.ToolTip = "PgUp"; _next.ToolTip = "PgDn";
        paging.Children.Add(_previous); paging.Children.Add(_pageLabel); paging.Children.Add(_next);
        paging.Children.Add(new TextBlock { Text = T("每页", "Per page"), Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        paging.Children.Add(_pageSizeChoice);
        paging.Children.Add(new TextBlock { Text = T("缩略图大小", "Thumbnail size"), Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center });
        paging.Children.Add(_thumbnailSize); footer.Children.Add(paging);
        footer.Children.Add(_count);
        footer.Children.Add(_status); Grid.SetRow(footer, 3); root.Children.Add(footer);
        _pages.Children.Add(root);
        Content = _pages;
        _onlyFavorites.Checked += (_, _) => { _page = 0; _selection.Retain(Filtered); RenderPage(); };
        _onlyFavorites.Unchecked += (_, _) => { _page = 0; RenderPage(); };
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = _detail is null && !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) => { if (_detail is null && e.Data.GetData(DataFormats.FileDrop) is string[] paths) await ImportPathsAsync(paths); e.Handled = true; };
        _pageSizeChoice.SelectionChanged += (_, _) =>
        {
            var filtered = Filtered;
            var anchorPath = _rows.Any(row => row.Path == _activePath) ? _activePath : _rows.FirstOrDefault()?.Path;
            var anchor = Array.IndexOf(filtered, anchorPath);
            _preferences.PhotoPageSize = PageSize; _preferences.Save(); _page = Math.Max(0, anchor / PageSize); RenderPage();
        };
        _thumbnailSize.ValueChanged += (_, _) => { foreach (var row in _rows) row.SetSize(_thumbnailSize.Value); };
        _thumbnailSize.PreviewMouseLeftButtonUp += (_, _) => SaveThumbnailSize();
        _thumbnailSize.PreviewKeyUp += (_, _) => SaveThumbnailSize();
        PreviewKeyDown += Keys;

        ReloadList();
        if (_files.Contains(current, StringComparer.OrdinalIgnoreCase)) { _selection.Select(current, _files); _page = _files.FindIndex(x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase)) / PageSize; RenderPage(); }
    }
    private Button Action(string text, Action run)
    {
        var button = new Button { Content = text, Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 0, 5, 4) };
        button.Click += (_, _) => run(); return button;
    }
    private async void OpenFolder()
    {
        if (_busy) return;
        var dialog = new OpenFolderDialog { Title = T("选择照片文件夹", "Choose a photo folder") };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await ImportPathsAsync([dialog.FolderName]);
    }
    internal async Task ImportPathsAsync(string[] paths)
    {
        if (_busy || paths.Length == 0) return;
        _busy = true; UpdateActions();
        _status.Text = T("正在读取照片…", "Reading photos…");
        try
        {
            var files = await Task.Run(() => paths.SelectMany(path => Directory.Exists(path) ? Directory.EnumerateFiles(path) : new[] { path })
                .Where(File.Exists).Where(ImageLoader.Supports).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(Path.GetFileName, NaturalStringComparer.Ordinal).ToArray());
            if (_closed) return;
            _files.Clear(); _files.AddRange(files); ScopeChanged = true;
            _folder.Text = paths.Length == 1 && Directory.Exists(paths[0]) ? Path.GetFullPath(paths[0]) : T("所选照片", "Selected photos"); _folder.ToolTip = _folder.Text;
            _onlyFavorites.IsChecked = false; _selection.Replace([]); _page = 0; RenderPage();
            _status.Text = files.Length == 0 ? T("此文件夹没有支持的图片。", "This folder contains no supported images.") : T("Ctrl 多选 · Shift 连选 · PgUp/PgDn 翻页", "Ctrl-click to multi-select · Shift-click for a range · PgUp/PgDn to turn pages");
        }
        catch (Exception e) { _status.Text = T("无法读取文件夹：", "Could not read folder: ") + e.Message; }
        finally { _busy = false; UpdateActions(); }
    }
    private void ReloadList()
    {
        try { _favorites = _store.Read(ListKey); _status.Text = T("收藏保存在本机，不改动原片；可导出文件名清单。", "Favorites stay on this computer without changing originals. Export the filenames when ready."); }
        catch (Exception e) { _favorites = []; _status.Text = T("无法读取收藏清单：", "Could not read favorites: ") + e.Message; }
        _selection.Replace([]); _page = 0; RenderPage();
    }
    private void TurnPage(int delta)
    {
        _page += delta; RenderPage();
        if (_rows.FirstOrDefault() is { } first) _grid.ScrollIntoView(first);
    }
    private void SaveThumbnailSize()
    {
        _preferences.PhotoThumbnailSize = _thumbnailSize.Value; _preferences.Save(); RenderPage();
    }
    internal void ClearImported()
    {
        if (_busy) return;
        _files.Clear(); _selection.Replace([]); _activePath = null; _page = 0; ScopeChanged = true;
        _folder.Text = ""; _folder.ToolTip = null; _onlyFavorites.IsChecked = false; RenderPage();
        _status.Text = T("已清空导入列表，文件和收藏已保留。", "Imported list cleared. Files and favorites were kept.");
    }
    private void RenderPage()
    {
        var filtered = Filtered; _selection.Retain(filtered); _page = Math.Clamp(_page, 0, Math.Max(0, (filtered.Length - 1) / PageSize));
        var generation = ++_generation;
        _updating = true;
        try { _rows.Clear(); foreach (var path in filtered.Skip(_page * PageSize).Take(PageSize)) _rows.Add(new Row(path, _favorites.Contains(path), T("收藏", "Favorite"), _thumbnailSize.Value, (p, value) => SaveFavorites([p], value))); }
        finally { _updating = false; }
        ApplySelection();
        var pixels = (int)Math.Clamp(_thumbnailSize.Value * VisualTreeHelper.GetDpi(this).DpiScaleX, 160, 720);
        _ = LoadThumbnailsAsync(_rows.ToArray(), generation, pixels);
    }
    private async Task LoadThumbnailsAsync(Row[] rows, int generation, int pixels)
    {
        await Task.WhenAll(rows.Select(async row =>
        {
            await _thumbnailGate.WaitAsync();
            try
            {
                if (_closed || generation != _generation) return;
                var image = await Task.Run(() => ImageLoader.LoadThumbnail(row.Path, pixels));
                if (!_closed && generation == _generation) row.SetThumbnail(image);
            }
            catch { /* A damaged or moved image remains selectable by its filename. */ }
            finally { _thumbnailGate.Release(); }
        }));
    }
    private void GridMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_busy || e.OriginalSource is not DependencyObject source) return;
        for (var p = source; p is not null && p != _grid; p = VisualTreeHelper.GetParent(p)) if (p is CheckBox) return;
        if (ItemsControl.ContainerFromElement(_grid, source) is not ListBoxItem { Content: Row row } item) return;
        _activePath = row.Path;
        var modifiers = Keyboard.Modifiers;
        _selection.Select(row.Path, Filtered, modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift));
        _updating = true; try { item.Focus(); } finally { _updating = false; }
        ApplySelection(); e.Handled = true;
        if (e.ClickCount == 2) PreviewSelected();
    }
    private void ApplySelection()
    {
        _updating = true;
        try { _grid.UnselectAll(); foreach (var row in _rows) if (_selection.Selected.Contains(row.Path)) _grid.SelectedItems.Add(row); }
        finally { _updating = false; }
        UpdateActions();
    }
    private void UpdateActions()
    {
        var n = _selection.Selected.Count; var total = Filtered.Length;
        _compare.IsEnabled = !_busy && _files.Count > 0; _preview.IsEnabled = !_busy && total > 0;
        _compare.ToolTip = T("选两张精确对比；选一张时从这张开始对比，可在对比页换图。", "Select two to compare exactly; with one selected, compare from that photo and choose another in the comparison view.");
        _selectedCount.Text = T($"已选 {n} 张", $"{n} selected");
        _clearImport.IsEnabled = !_busy && _files.Count > 0; _pageSizeChoice.IsEnabled = _thumbnailSize.IsEnabled = !_busy;
        _favorite.IsEnabled = _unfavorite.IsEnabled = !_busy && n > 0; _trash.IsEnabled = !_busy && n > 0;
        _export.IsEnabled = !_busy && _files.Any(_favorites.Contains); _openFolder.IsEnabled = _onlyFavorites.IsEnabled = _grid.IsEnabled = !_busy;
        _empty.Text = _files.Count == 0 ? T("打开或拖入文件夹，开始选片", "Open or drop a folder to start selecting photos") : T("还没有收藏照片，取消“只看收藏”可查看全部。", "No favorites yet. Turn off Favorites only to see all photos.");
        _empty.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!ScopeChanged) { _folder.Text = _files.Count > 0 ? Path.GetDirectoryName(_files[0]) ?? "" : ""; _folder.ToolTip = _folder.Text; }
        _previous.IsEnabled = !_busy && _page > 0; _next.IsEnabled = !_busy && (_page + 1) * PageSize < total;
        var pageCount = (total + PageSize - 1) / PageSize;
        _pageLabel.Text = $"{(pageCount == 0 ? 0 : _page + 1)} / {pageCount}";
        _count.Text = T($"{total} 张照片 · 收藏 {_files.Count(_favorites.Contains)} 张", $"{total} photos · {_files.Count(_favorites.Contains)} favorites");
    }
    private bool SaveFavorites(IEnumerable<string> paths, bool favorite)
    {
        if (_busy) return false;
        try
        {
            _favorites = _store.Set(ListKey, paths.ToArray(), favorite);
            foreach (var row in _rows) row.SetFavorite(_favorites.Contains(row.Path));
            if (_onlyFavorites.IsChecked == true) { _selection.Retain(Filtered); RenderPage(); } else UpdateActions();
            return true;
        }
        catch (Exception e) { _status.Text = T("收藏未能保存，原清单未被覆盖：", "Favorites could not be saved; the original list was preserved: ") + e.Message; return false; }
    }
    internal (string[] Files, string Current)? BuildComparison()
    {
        var filtered = Filtered; if (filtered.Length < 2) return null;
        var chosen = _selection.Ordered(filtered);
        var files = chosen.Length >= 2 ? chosen : filtered;
        var current = chosen.FirstOrDefault() ?? (_activePath is not null && files.Contains(_activePath) ? _activePath : files[0]);
        return (files, current);
    }
    private void CompareSelected()
    {
        if (_busy) return;
        var request = BuildComparison();
        if (request is null)
        {
            _selectedCount.Text = T("至少需要两张照片", "At least two photos needed");
            _status.Text = _onlyFavorites.IsChecked == true ? T("收藏筛选中不足两张；取消“只看收藏”即可选择其他照片。", "Fewer than two favorites. Turn off Favorites only to choose more photos.") : T("打开至少两张照片即可对比。", "Open at least two photos to compare.");
            return;
        }
        var comparison = new ReviewView(request.Value.Files, request.Value.Current, _zh, _store, ListKey);
        comparison.BackRequested += () =>
        {
            ReturnToGrid();
            RefreshFavorites();
        };
        ShowDetail(comparison);
    }
    private void RefreshFavorites()
    {
        try { _favorites = _store.Read(ListKey); foreach (var row in _rows) row.SetFavorite(_favorites.Contains(row.Path)); if (_onlyFavorites.IsChecked == true) RenderPage(); else UpdateActions(); } catch (Exception e) { _status.Text = e.Message; }
    }
    private void PreviewSelected()
    {
        var files = Filtered; if (_busy || files.Length == 0) return;
        var selected = _selection.Ordered(files);
        var current = _activePath is not null && selected.Contains(_activePath) ? _activePath : selected.FirstOrDefault() ?? _rows.FirstOrDefault()?.Path ?? files[0];
        var preview = new PhotoPreviewView(files, current, _zh, recycle: DeletePreviewPhotoAsync);
        preview.BackRequested += () =>
        {
            ReturnToGrid();
            if (!StringComparer.OrdinalIgnoreCase.Equals(current, preview.CurrentPath) || !_files.Contains(current))
                FocusPhoto(preview.CurrentPath);
        };
        ShowDetail(preview);
    }
    private void ShowDetail(FrameworkElement detail)
    {
        _detail = detail;
        _gridPage.Visibility = Visibility.Collapsed;
        _pages.Children.Add(detail);
        Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_detail, detail)) detail.Focus(); });
    }
    private void ReturnToGrid()
    {
        if (_detail is not { } detail) return;
        if (detail is IDisposable disposable) disposable.Dispose();
        _pages.Children.Remove(detail);
        _detail = null;
        _gridPage.Visibility = Visibility.Visible;
        _grid.Focus();
    }
    public void Dispose()
    {
        _closed = true;
        _generation++;
        if (_detail is IDisposable detail) detail.Dispose();
    }
    internal void FocusPhoto(string path)
    {
        var files = Filtered; if (!files.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        _activePath = path; _selection.Replace([path]); _page = Math.Max(0, Array.IndexOf(files, path) / PageSize); RenderPage();
        var row = _rows.FirstOrDefault(r => StringComparer.OrdinalIgnoreCase.Equals(r.Path, _activePath));
        if (row is not null) _grid.ScrollIntoView(row); _grid.Focus();
    }
    internal void RemoveSelectedFromScope()
    {
        if (_busy || _selection.Selected.Count == 0) return;
        var count = _files.RemoveAll(_selection.Selected.Contains); ScopeChanged |= count > 0;
        _selection.Replace([]); _activePath = null; RenderPage();
        _status.Text = T($"已移出 {count} 张，文件和收藏已保留。", $"Removed {count} from this selection. Files and favorites were kept.");
    }
    private async void ChooseMoveFolder()
    {
        var count = _selection.Selected.Count; if (_busy || count == 0) return;
        var dialog = new OpenFolderDialog { Title = T($"将 {count} 张照片移动到…", $"Move {count} photos to…") };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await MoveSelectedAsync(dialog.FolderName);
    }
    internal async Task MoveSelectedAsync(string destination)
    {
        var paths = _selection.Ordered(_files); if (_busy || paths.Length == 0) return;
        _busy = true; UpdateActions(); _status.Text = T("正在移动照片…", "Moving photos…");
        try
        {
            var result = await Task.Run(() =>
            {
                var folder = Path.GetFullPath(destination);
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException();
                var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); int conflicts = 0, failed = 0;
                foreach (var path in paths)
                {
                    try
                    {
                        var target = Path.Combine(folder, Path.GetFileName(path));
                        if (StringComparer.OrdinalIgnoreCase.Equals(target, path) || File.Exists(target) || Directory.Exists(target)) { conflicts++; continue; }
                        if (!ImageLoader.Supports(path) || !File.Exists(path)) { failed++; continue; }
                        File.Move(path, target, overwrite: false);
                        if (!File.Exists(path) && File.Exists(target)) moved.Add(path, target); else failed++;
                    }
                    catch { failed++; }
                }
                return (Moved: moved, Conflicts: conflicts, Failed: failed);
            });
            _moved.UnionWith(result.Moved.Keys); _files.RemoveAll(result.Moved.ContainsKey); ScopeChanged |= result.Moved.Count > 0;
            var favoriteFailed = false;
            if (result.Moved.Count > 0)
                try { _favorites = await Task.Run(() => _store.Relocate(ListKey, result.Moved)); } catch { favoriteFailed = true; }
            _selection.Retain(_files); RenderPage();
            _status.Text = T($"已移动 {result.Moved.Count} 张；同名跳过 {result.Conflicts} 张；失败 {result.Failed} 张。", $"Moved {result.Moved.Count}; skipped {result.Conflicts} existing names; {result.Failed} failed.");
            if (favoriteFailed) _status.Text += T(" 收藏记录未能更新。", " Favorites could not be updated.");
        }
        catch { _status.Text = T("无法移动照片，请检查目标文件夹。", "Could not move photos. Check the destination folder."); }
        finally { _busy = false; UpdateActions(); }
    }
    internal Task DeleteSelectedAsync(Func<int, bool>? confirm = null) => RecyclePhotosAsync(_selection.Ordered(_files), confirm);
    private async Task<bool> DeletePreviewPhotoAsync(string path)
    {
        await RecyclePhotosAsync([path]);
        return !_files.Contains(path, StringComparer.OrdinalIgnoreCase);
    }
    private async Task RecyclePhotosAsync(string[] paths, Func<int, bool>? confirm = null)
    {
        if (_busy || _closed || paths.Length == 0) return;
        confirm ??= count =>
        {
            var prompt = count == 1
                ? T($"将「{Path.GetFileName(paths[0])}」移入回收站？", $"Move “{Path.GetFileName(paths[0])}” to the Recycle Bin?")
                : T($"将所选 {count} 张照片移入回收站？", $"Move {count} selected photos to the Recycle Bin?");
            return MessageBox.Show(Window.GetWindow(this), prompt, T("移入回收站", "Move to Recycle Bin"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        };
        if (!confirm(paths.Length)) return;
        _busy = true; UpdateActions();
        try
        {
            var result = await _recycle(paths, Window.GetWindow(this) is { } owner ? new WindowInteropHelper(owner).Handle : IntPtr.Zero);
            foreach (var path in result.Recycled) { _files.RemoveAll(p => StringComparer.OrdinalIgnoreCase.Equals(p, path)); _recycled.Add(path); }
            _selection.Retain(_files); RenderPage();
            _status.Text = T($"已移入回收站 {result.Recycled.Count} 张；失败 {result.Failed.Count} 张。", $"Moved {result.Recycled.Count} to Recycle Bin; {result.Failed.Count} failed.") + (result.Cancelled ? T(" 操作已取消，剩余照片未处理。", " Cancelled; remaining photos were not processed.") : "");
        }
        catch (Exception e) { _status.Text = T("未能完成回收站操作：", "Could not complete the Recycle Bin operation: ") + e.Message; }
        finally { _busy = false; UpdateActions(); }
    }
    private void ExportFavorites()
    {
        var paths = _files.Where(_favorites.Contains).ToArray(); if (paths.Length == 0 || _busy) return;
        var dialog = new SaveFileDialog { Filter = "Text (*.txt)|*.txt", DefaultExt = ".txt", FileName = T("选片清单.txt", "photo-selection.txt") };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllLines(dialog.FileName, paths.Select(p => Path.GetFileName(p) ?? p), new System.Text.UTF8Encoding(true)); _status.Text = T("收藏文件名清单已导出。", "Favorite filenames exported."); } catch (Exception e) { _status.Text = e.Message; }
    }
    private void Keys(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (_detail is not null || e.OriginalSource is TextBox || Keyboard.FocusedElement is TextBox or ComboBox or Slider || _busy) return;
        if (key is Key.Space or Key.Enter && Keyboard.FocusedElement is Button or CheckBox) return;
        if (key == Key.O && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) OpenFolder();
        else if (key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { _selection.Replace(Filtered); ApplySelection(); }
        else if (key == Key.Delete) _ = DeleteSelectedAsync();
        else if (key == Key.P) SaveFavorites(_selection.Selected, true);
        else if (key == Key.U) SaveFavorites(_selection.Selected, false);
        else if (key == Key.C) CompareSelected();
        else if (key == Key.Space && Keyboard.Modifiers == ModifierKeys.None) PreviewSelected();
        else if (key == Key.PageDown && _next.IsEnabled) TurnPage(1);
        else if (key == Key.PageUp && _previous.IsEnabled) TurnPage(-1);
        else if (key == Key.Escape) BackRequested?.Invoke();
        else return;
        e.Handled = true;
    }
    private sealed class Row : INotifyPropertyChanged
    {
        public string Path { get; }
        public string Name => System.IO.Path.GetFileName(Path);
        public string FavoriteLabel { get; }
        private bool _favorite;
        private readonly Func<string, bool, bool> _save;
        public bool Favorite { get => _favorite; set { if (value != _favorite) _save(Path, value); Changed(nameof(Favorite)); } }
        public double CardWidth { get; private set; }
        public double PreviewHeight => Math.Round(CardWidth * 0.7);
        public void SetSize(double value) { CardWidth = value; Changed(nameof(CardWidth)); Changed(nameof(PreviewHeight)); }
        public BitmapSource? Thumbnail { get; private set; }
        public Row(string path, bool favorite, string label, double size, Func<string, bool, bool> save) { CardWidth = size; Path = path; _favorite = favorite; FavoriteLabel = label; _save = save; }
        public void SetFavorite(bool value) { _favorite = value; Changed(nameof(Favorite)); }
        public void SetThumbnail(BitmapSource image) { Thumbnail = image; Changed(nameof(Thumbnail)); }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
