using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using YingView.Core;
using YingView.Imaging;

namespace YingView.Review;

internal sealed class PhotoPreviewWindow : Window
{
    private readonly ImageSurface _surface = new() { CanvasMode = "black" };
    private readonly string[] _files;
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
    internal PhotoPreviewWindow(string path, bool chinese) : this([path], path, chinese) { }
    internal PhotoPreviewWindow(string[] files, string current, bool chinese, Func<string, Task<ImageDocument>>? load = null)
    {
        _zh = chinese;
        _files = files.Where(ImageLoader.Supports).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (_files.Length == 0) throw new ArgumentException("No images to preview.", nameof(files));
        _index = Math.Max(0, Array.FindIndex(_files, p => StringComparer.OrdinalIgnoreCase.Equals(p, current)));
        _load = load ?? (path => Task.Run(() => ImageLoader.Load(path)));
        Width = 1080; Height = 760; MinWidth = 640; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.Black;
        Resources["TextBrush"] = Brushes.White; Resources["HoverBrush"] = ViewerTheme.Brush("#293138");
        var root = new DockPanel();
        var toolbar = new DockPanel { Background = ViewerTheme.Brush("#1B2025"), Margin = new Thickness(0), LastChildFill = true };
        var back = new Button { Content = T("返回选片 · Esc", "Back to selection · Esc"), Padding = new Thickness(12, 9, 12, 9) };
        back.Click += (_, _) => Close(); DockPanel.SetDock(back, Dock.Right); toolbar.Children.Add(back);
        var arrows = new StackPanel { Orientation = Orientation.Horizontal };
        _previous = new Button { Content = T("← 上一张", "← Previous"), Padding = new Thickness(12, 9, 12, 9) };
        _next = new Button { Content = T("下一张 →", "Next →"), Padding = new Thickness(12, 9, 12, 9) };
        _previous.Click += async (_, _) => await NavigateAsync(-1); _next.Click += async (_, _) => await NavigateAsync(1);
        arrows.Children.Add(_previous); arrows.Children.Add(_next); toolbar.Children.Add(arrows); toolbar.Children.Add(_position);
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var stage = new Grid(); stage.Children.Add(_surface); stage.Children.Add(_message); root.Children.Add(stage); Content = root;
        PreviewKeyDown += async (_, e) =>
        {
            var key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            switch (key)
            {
                case Key.Escape: case Key.Space: e.Handled = true; Close(); break;
                case Key.Left: case Key.PageUp: e.Handled = true; await NavigateAsync(-1); break;
                case Key.Right: case Key.PageDown: e.Handled = true; await NavigateAsync(1); break;
                case Key.Home: e.Handled = true; await NavigateAsync(-_index); break;
                case Key.End: e.Handled = true; await NavigateAsync(_files.Length - 1 - _index); break;
                case Key.F: e.Handled = true; _surface.Fit(ViewportFitMode.Contain); break;
            }
        };
        Loaded += async (_, _) => await LoadCurrentAsync();
        Closed += (_, _) => { _closed = true; ++_generation; ClearImage(); };
        UpdatePosition();
    }
    private void UpdatePosition()
    {
        Title = Path.GetFileName(CurrentPath);
        _position.Text = $"{_index + 1} / {_files.Length}   {Path.GetFileName(CurrentPath)}";
        _previous.IsEnabled = _index > 0; _next.IsEnabled = _index + 1 < _files.Length;
    }
    private void ClearImage()
    {
        var old = _surface.Document; _surface.SetDocument(null, ViewportFitMode.Contain); old?.Dispose();
    }
    internal Task NavigateAsync(int delta)
    {
        if (_closed) return Task.CompletedTask;
        var next = Math.Clamp(_index + delta, 0, _files.Length - 1);
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
