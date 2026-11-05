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
    private bool _closed;
    internal PhotoPreviewWindow(string path, bool chinese)
    {
        Title = Path.GetFileName(path); Width = 1080; Height = 760; MinWidth = 600; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.Black;
        Resources["TextBrush"] = Brushes.White; Resources["HoverBrush"] = ViewerTheme.Brush("#293138");
        var root = new DockPanel();
        var close = new Button { Content = chinese ? "返回选片 · Esc" : "Back to selection · Esc", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Top); root.Children.Add(close); root.Children.Add(_surface); Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Space) { Close(); e.Handled = true; } else if (e.Key == Key.F) { _surface.Fit(ViewportFitMode.Contain); e.Handled = true; } };
        Loaded += async (_, _) =>
        {
            try { var image = await Task.Run(() => ImageLoader.Load(path)); if (_closed) image.Dispose(); else _surface.SetDocument(image, ViewportFitMode.Contain); }
            catch { if (!_closed) { Title += chinese ? " · 无法读取图片" : " · Could not read image"; } }
        };
        Closed += (_, _) => { _closed = true; var image = _surface.Document; _surface.SetDocument(null, ViewportFitMode.Contain); image?.Dispose(); };
    }
}
