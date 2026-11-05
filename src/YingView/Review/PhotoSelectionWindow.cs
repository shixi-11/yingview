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

public sealed class PhotoSelectionWindow : Window
{
    private const int PageSize = 120;
    private readonly bool _zh;
    private readonly List<string> _files;
    private readonly PhotoSelection _selection = new();
    private readonly SemaphoreSlim _thumbnailGate = new(3);
    private readonly PhotoSelectionStore _store;
    private readonly Func<IEnumerable<string>, IntPtr, Task<RecycleBinService.BatchResult>> _recycle;
    private readonly ObservableCollection<Row> _rows = [];
    private readonly ListBox _grid = new() { SelectionMode = SelectionMode.Extended, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly CheckBox _onlyFavorites = new() { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _compare, _preview, _favorite, _unfavorite, _trash, _export, _previous, _next;
    private HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _recycled = new(StringComparer.OrdinalIgnoreCase);
    private bool _updating, _busy, _closed;
    private int _page, _generation;
    private const string ListKey = "favorites";
    private readonly TextBlock _folder = new() { TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
    private readonly TextBlock _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 18, Foreground = Brushes.LightGray, TextAlignment = TextAlignment.Center };
    private readonly Button _openFolder;
    public bool ScopeChanged { get; private set; }
    public IReadOnlyList<string> CurrentFiles => _files;
    public IReadOnlyCollection<string> RecycledFiles => _recycled;
    private string T(string zh, string en) => _zh ? zh : en;
    private string[] Filtered => _files.Where(p => _onlyFavorites.IsChecked != true || _favorites.Contains(p)).ToArray();

    public PhotoSelectionWindow(string[] files, string current, bool chinese, PhotoSelectionStore? store = null,
        Func<IEnumerable<string>, IntPtr, Task<RecycleBinService.BatchResult>>? recycle = null)
    {
        _zh = chinese; _store = store ?? new(); _recycle = recycle ?? RecycleBinService.RecycleManyAsync;
        _files = files.Where(ImageLoader.Supports).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Title = T("选片 · 映窗", "Photo selection · YingView"); Width = 1120; Height = 800; MinWidth = 720; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ViewerTheme.Brush("#111417"); Foreground = Brushes.White;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 13;
        Resources["BarBrush"] = ViewerTheme.Brush("#1B2025"); Resources["LineBrush"] = ViewerTheme.Brush("#333A41");
        Resources["TextBrush"] = Brushes.White; Resources["MutedBrush"] = Brushes.LightGray;
        Resources["HoverBrush"] = ViewerTheme.Brush("#303A3E"); Resources["PressedBrush"] = ViewerTheme.Brush("#202824");
        var root = new Grid { Margin = new Thickness(16), Background = Background };
        TextElement.SetForeground(root, Brushes.White);
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = T("选片", "Photo selection"), FontSize = 22, FontWeight = FontWeights.SemiBold });
        var options = new DockPanel { Margin = new Thickness(0, 12, 0, 10) };
        _openFolder = Action(T("打开文件夹…", "Open folder…"), OpenFolder);
        _openFolder.Background = ViewerTheme.Brush("#2B3944"); _openFolder.Foreground = Brushes.White;
        DockPanel.SetDock(_openFolder, Dock.Left); options.Children.Add(_openFolder);
        _onlyFavorites.Content = T("只看收藏", "Favorites only"); DockPanel.SetDock(_onlyFavorites, Dock.Right); options.Children.Add(_onlyFavorites);
        options.Children.Add(_folder); heading.Children.Add(options); root.Children.Add(heading);
        _empty.Text = T("打开或拖入文件夹，开始选片", "Open or drop a folder to start selecting photos");
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        tools.Children.Add(Action(T("全选", "Select all"), () => { _selection.Replace(Filtered); ApplySelection(); }));
        tools.Children.Add(Action(T("清空选择", "Clear selection"), () => { _selection.Replace([]); ApplySelection(); }));
        _favorite = Action(T("收藏所选", "Favorite"), () => SaveFavorites(_selection.Selected, true)); tools.Children.Add(_favorite);
        _unfavorite = Action(T("取消收藏", "Unfavorite"), () => SaveFavorites(_selection.Selected, false)); tools.Children.Add(_unfavorite);
        _compare = Action(T("对比两张", "Compare two"), CompareSelected); tools.Children.Add(_compare);
        _preview = Action(T("大图", "Preview"), PreviewSelected); tools.Children.Add(_preview);
        _trash = Action(T("移入回收站", "Move to Recycle Bin"), async () => await DeleteSelectedAsync()); tools.Children.Add(_trash);
        _export = Action(T("导出收藏清单…", "Export favorites…"), ExportFavorites); tools.Children.Add(_export);
        Grid.SetRow(tools, 1); root.Children.Add(tools);
        ScrollViewer.SetHorizontalScrollBarVisibility(_grid, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_grid, ScrollBarVisibility.Auto);
        _grid.ItemsSource = _rows;
        _grid.ItemsPanel = (ItemsPanelTemplate)XamlReader.Parse("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><WrapPanel/></ItemsPanelTemplate>");
        _grid.ItemTemplate = (DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Width='154'><Border Height='108' Background='#090B0D' CornerRadius='5'><Image Source='{Binding Thumbnail}' Stretch='Uniform' Margin='3'/></Border><TextBlock Text='{Binding Name}' ToolTip='{Binding Name}' Foreground='#EDF0F2' TextTrimming='CharacterEllipsis' Margin='2,7,2,4'/><CheckBox Content='{Binding FavoriteLabel}' IsChecked='{Binding Favorite,Mode=TwoWay}' Foreground='#91D9C5' Margin='2,0,2,3'/></StackPanel></DataTemplate>");
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(3))); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, ViewerTheme.Brush("#1B2025"))); style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, ViewerTheme.Brush("#91D9C5"))); selected.Setters.Add(new Setter(Control.BackgroundProperty, ViewerTheme.Brush("#283832"))); style.Triggers.Add(selected);
        _grid.ItemContainerStyle = style;
        _grid.PreviewMouseLeftButtonDown += GridMouseDown;
        _grid.SelectionChanged += (_, _) => { if (!_updating) { var visibleSelection = _grid.SelectedItems.Cast<Row>().Select(r => r.Path).ToArray(); var chosen = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? _selection.Selected.Except(_rows.Select(r => r.Path), StringComparer.OrdinalIgnoreCase).Concat(visibleSelection).ToArray() : visibleSelection; _selection.Replace(chosen); UpdateActions(); } };
        var content = new Grid(); content.Children.Add(_grid); content.Children.Add(_empty);
        Grid.SetRow(content, 2); root.Children.Add(content);
        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var paging = new DockPanel(); var pages = new StackPanel { Orientation = Orientation.Horizontal };
        _previous = Action("‹", () => { _page--; RenderPage(); }); _next = Action("›", () => { _page++; RenderPage(); }); pages.Children.Add(_previous); pages.Children.Add(_next);
        DockPanel.SetDock(pages, Dock.Right); paging.Children.Add(pages); paging.Children.Add(_count); footer.Children.Add(paging);
        footer.Children.Add(new TextBlock { Text = T("Ctrl 多选 · Shift 连选 · Ctrl+A 全选 · 空格大图 · C 对比 · P 收藏 · U 取消收藏", "Ctrl multi-select · Shift range · Ctrl+A select all · Space preview · C compare · P favorite · U unfavorite"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 5, 0, 0) });
        footer.Children.Add(_status); Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
        _onlyFavorites.Checked += (_, _) => { _page = 0; _selection.Retain(Filtered); RenderPage(); };
        _onlyFavorites.Unchecked += (_, _) => { _page = 0; RenderPage(); };
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await ImportPathsAsync(paths); e.Handled = true; };
        PreviewKeyDown += Keys;
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
        Closed += (_, _) => { _closed = true; _generation++; };
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
        if (dialog.ShowDialog(this) == true) await ImportPathsAsync([dialog.FolderName]);
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
            _status.Text = files.Length == 0 ? T("此文件夹没有支持的图片。", "This folder contains no supported images.") : T("Ctrl 多选，Shift 连选；选两张即可对比。", "Ctrl-click to select, Shift-click for a range. Select two to compare.");
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
    private void RenderPage()
    {
        var filtered = Filtered; _selection.Retain(filtered); _page = Math.Clamp(_page, 0, Math.Max(0, (filtered.Length - 1) / PageSize));
        var generation = ++_generation;
        _updating = true;
        try { _rows.Clear(); foreach (var path in filtered.Skip(_page * PageSize).Take(PageSize)) _rows.Add(new Row(path, _favorites.Contains(path), T("收藏", "Favorite"), (p, value) => SaveFavorites([p], value))); }
        finally { _updating = false; }
        ApplySelection();
        _ = LoadThumbnailsAsync(_rows.ToArray(), generation);
    }
    private async Task LoadThumbnailsAsync(Row[] rows, int generation)
    {
        await Task.WhenAll(rows.Select(async row =>
        {
            await _thumbnailGate.WaitAsync();
            try
            {
                if (_closed || generation != _generation) return;
                var image = await Task.Run(() => ImageLoader.LoadThumbnail(row.Path, 160));
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
        var n = _selection.Selected.Count;
        _compare.IsEnabled = !_busy && n == 2; _preview.IsEnabled = !_busy && n == 1;
        _favorite.IsEnabled = _unfavorite.IsEnabled = !_busy && n > 0; _trash.IsEnabled = !_busy && n > 0;
        _export.IsEnabled = !_busy && _files.Any(_favorites.Contains); _openFolder.IsEnabled = _onlyFavorites.IsEnabled = _grid.IsEnabled = !_busy;
        _empty.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!ScopeChanged) { _folder.Text = _files.Count > 0 ? Path.GetDirectoryName(_files[0]) ?? "" : ""; _folder.ToolTip = _folder.Text; }
        var total = Filtered.Length; _previous.IsEnabled = !_busy && _page > 0; _next.IsEnabled = !_busy && (_page + 1) * PageSize < total;
        _count.Text = T($"已选 {n} 张 · 收藏 {_files.Count(_favorites.Contains)} 张 · {total} 张照片 · 第 {_page + 1}/{Math.Max(1, (total + PageSize - 1) / PageSize)} 页", $"{n} selected · {_files.Count(_favorites.Contains)} favorites · {total} photos · Page {_page + 1}/{Math.Max(1, (total + PageSize - 1) / PageSize)}");
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
    private void CompareSelected()
    {
        var pair = _selection.Ordered(_files); if (_busy || pair.Length != 2) return;
        new ReviewWindow(pair, pair[0], _zh, _store, ListKey) { Owner = this }.ShowDialog();
        try { _favorites = _store.Read(ListKey); foreach (var row in _rows) row.SetFavorite(_favorites.Contains(row.Path)); if (_onlyFavorites.IsChecked == true) RenderPage(); else UpdateActions(); } catch (Exception e) { _status.Text = e.Message; }
    }
    private void PreviewSelected()
    {
        var paths = _selection.Ordered(_files); if (_busy || paths.Length != 1) return;
        new PhotoPreviewWindow(paths[0], _zh) { Owner = this }.ShowDialog();
    }
    internal async Task DeleteSelectedAsync(Func<int, bool>? confirm = null)
    {
        var paths = _selection.Ordered(_files); if (_busy || paths.Length == 0) return;
        confirm ??= count => MessageBox.Show(this, T($"将所选 {count} 张照片移入 Windows 回收站？\n可在回收站中恢复。", $"Move {count} selected photos to the Windows Recycle Bin?\nThey can be restored from the Recycle Bin."), T("移入回收站", "Move to Recycle Bin"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (!confirm(paths.Length)) return;
        _busy = true; UpdateActions();
        try
        {
            var result = await _recycle(paths, new WindowInteropHelper(this).Handle);
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
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllLines(dialog.FileName, paths.Select(p => Path.GetFileName(p) ?? p), new System.Text.UTF8Encoding(true)); _status.Text = T("收藏文件名清单已导出。", "Favorite filenames exported."); } catch (Exception e) { _status.Text = e.Message; }
    }
    private void Keys(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (e.OriginalSource is TextBox || Keyboard.FocusedElement is TextBox or ComboBox || _busy) return;
        if (key is Key.Space or Key.Enter && Keyboard.FocusedElement is Button or CheckBox) return;
        if (key == Key.O && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) OpenFolder();
        else if (key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { _selection.Replace(Filtered); ApplySelection(); }
        else if (key == Key.Delete) _ = DeleteSelectedAsync();
        else if (key == Key.P) SaveFavorites(_selection.Selected, true);
        else if (key == Key.U) SaveFavorites(_selection.Selected, false);
        else if (key == Key.C) CompareSelected();
        else if (key == Key.Space) PreviewSelected();
        else if (key == Key.Escape) Close();
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
        public BitmapSource? Thumbnail { get; private set; }
        public Row(string path, bool favorite, string label, Func<string, bool, bool> save) { Path = path; _favorite = favorite; FavoriteLabel = label; _save = save; }
        public void SetFavorite(bool value) { _favorite = value; Changed(nameof(Favorite)); }
        public void SetThumbnail(BitmapSource image) { Thumbnail = image; Changed(nameof(Thumbnail)); }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
