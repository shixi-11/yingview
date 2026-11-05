using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using YingView.Core;
using YingView.Imaging;

namespace YingView.Review;

internal sealed class PhotoPreviewView : UserControl, IDisposable
{
    private readonly ImageSurface _surface = new() { CanvasMode = "black" };
    private readonly List<string> _files;
    private readonly Func<string, Task<bool>>? _recycle;
    internal event Action? BackRequested;
    private bool _deleting;
    private readonly bool _zh;
    private readonly Func<string, Task<ImageDocument>> _load;
    private readonly Button _previous, _next;
    private readonly TextBlock _position = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 12, 0) };
    private readonly TextBlock _message = new() { Foreground = Brushes.LightGray, FontSize = 16, MaxWidth = 500, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private bool _closed;
    private int _index, _generation;
    internal string CurrentPath => _files[_index];
    internal ImageDocument? CurrentDocument => _surface.Document;
    private string T(string zh, string en) => _zh ? zh : en;
    internal PhotoPreviewView(string path, bool chinese) : this([path], path, chinese) { }
    internal PhotoPreviewView(string[] files, string current, bool chinese, Func<string, Task<ImageDocument>>? load = null, Func<string, Task<bool>>? recycle = null)
    {
        _zh = chinese;
        _recycle = recycle;
        _files = files.Where(ImageLoader.Supports).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (_files.Count == 0) throw new ArgumentException("No images to preview.", nameof(files));
        _index = Math.Max(0, _files.FindIndex(p => StringComparer.OrdinalIgnoreCase.Equals(p, current)));
        _load = load ?? (path => Task.Run(() => ImageLoader.Load(path)));
        Focusable = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Background = Brushes.Black;
        Resources["TextBrush"] = Brushes.White; Resources["HoverBrush"] = ViewerTheme.Brush("#293138");
        var root = new DockPanel();
        var toolbar = new DockPanel { Background = ViewerTheme.Brush("#1B2025"), Margin = new Thickness(0), LastChildFill = true };
        var back = new Button { Content = T("返回选片 · Esc", "Back to selection · Esc"), Padding = new Thickness(12, 9, 12, 9) };
        back.Click += (_, _) => BackRequested?.Invoke(); DockPanel.SetDock(back, Dock.Right); toolbar.Children.Add(back);
        var arrows = new StackPanel { Orientation = Orientation.Horizontal };
        _previous = new Button { Content = T("← 上一张", "← Previous"), Padding = new Thickness(12, 9, 12, 9) };
        _next = new Button { Content = T("下一张 →", "Next →"), Padding = new Thickness(12, 9, 12, 9) };
        _previous.Click += async (_, _) => await NavigateAsync(-1); _next.Click += async (_, _) => await NavigateAsync(1);
        arrows.Children.Add(_previous); arrows.Children.Add(_next);
        var actual = new Button { Content = "100%", ToolTip = T("原始像素 · 1", "Actual pixels · 1"), Padding = new Thickness(10, 9, 10, 9) };
        actual.Click += (_, _) => _surface.Fit(ViewportFitMode.Actual);
        arrows.Children.Add(actual);
        if (_recycle is not null)
        {
            var trash = new Button { Content = T("移入回收站…", "Move to Recycle Bin…"), Padding = new Thickness(10, 9, 10, 9) };
            trash.Click += async (_, _) => await DeleteCurrentAsync();
            arrows.Children.Add(trash);
        }
        toolbar.Children.Add(arrows); toolbar.Children.Add(_position);
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var stage = new Grid(); stage.Children.Add(_surface); stage.Children.Add(_message); root.Children.Add(stage); Content = root;
        PreviewKeyDown += async (_, e) =>
        {
            if (_deleting || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;
            var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            switch (key)
            {
                case Key.Escape: e.Handled = true; BackRequested?.Invoke(); break;
                case Key.Space when Keyboard.FocusedElement is not Button: e.Handled = true; BackRequested?.Invoke(); break;
                case Key.Delete when _recycle is not null: e.Handled = true; await DeleteCurrentAsync(); break;
                case Key.D1: case Key.NumPad1: e.Handled = true; _surface.Fit(ViewportFitMode.Actual); break;
                case Key.Left: case Key.PageUp: e.Handled = true; await NavigateAsync(-1); break;
                case Key.Right: case Key.PageDown: e.Handled = true; await NavigateAsync(1); break;
                case Key.Home: e.Handled = true; await NavigateAsync(-_index); break;
                case Key.End: e.Handled = true; await NavigateAsync(_files.Count - 1 - _index); break;
                case Key.F: e.Handled = true; _surface.Fit(ViewportFitMode.Contain); break;
            }
        };
        Loaded += async (_, _) => { if (_generation == 0) await LoadCurrentAsync(); };
        UpdatePosition();
    }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        ++_generation;
        ClearImage();
    }
    internal async Task DeleteCurrentAsync()
    {
        if (_deleting || _closed || _recycle is null) return;
        _deleting = true;
        IsEnabled = false;
        try
        {
            if (!await _recycle(CurrentPath)) return;
            if (_files.Count == 1)
            {
                BackRequested?.Invoke();
                return;
            }
            _files.RemoveAt(_index);
            _index = Math.Min(_index, _files.Count - 1);
            await LoadCurrentAsync();
        }
        finally
        {
            _deleting = false;
            if (!_closed) { IsEnabled = true; Focus(); }
        }
    }
    private void UpdatePosition()
    {
        _position.Text = $"{_index + 1} / {_files.Count}   {Path.GetFileName(CurrentPath)}";
        _previous.IsEnabled = _index > 0; _next.IsEnabled = _index + 1 < _files.Count;
    }
    private void ClearImage()
    {
        var old = _surface.Document; _surface.SetDocument(null, ViewportFitMode.Contain); old?.Dispose();
    }
    internal Task NavigateAsync(int delta)
    {
        if (_closed || _deleting) return Task.CompletedTask;
        var next = Math.Clamp(_index + delta, 0, _files.Count - 1);
        if (next == _index) return Task.CompletedTask;
        _index = next; return LoadCurrentAsync();
    }
    internal async Task LoadCurrentAsync()
    {
        if (_closed) return;
        var generation = ++_generation; var path = CurrentPath;
        UpdatePosition(); ClearImage(); _message.Text = T("正在打开…", "Opening…"); _message.Visibility = Visibility.Visible;
        try
        {
            var image = await _load(path);
            if (_closed || generation != _generation) { image.Dispose(); return; }
            _surface.SetDocument(image, ViewportFitMode.Contain); _message.Visibility = Visibility.Collapsed;
        }
        catch
        {
            if (_closed || generation != _generation) return;
            _message.Text = T("无法读取这张图片。可以继续翻图，或返回选片。", "Could not read this image. Continue browsing or return to selection.");
        }
    }
}
