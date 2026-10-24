using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using YingView.Core;
using YingView.Imaging;

namespace YingView;

public sealed class ImageSurface : FrameworkElement
{
    public Viewport View { get; } = new();
    public ImageDocument? Document { get; private set; }
    public Brush CanvasBrush { get; set; } = Brushes.Black;
    public string CanvasMode { get; set; } = "theme";
    public string AlphaBackground { get; set; } = "dark";
    public bool Mirrored { get; private set; }
    public bool AnimationPaused { get; private set; }
    public event Action? ViewChanged;
    public event Action? FullscreenRequested;
    private readonly DispatcherTimer _animation = new(DispatcherPriority.Render);
    private int _frame;
    private int _loops;
    private Point? _drag;
    private static readonly Brush Checker = CreateChecker();

    public ImageSurface()
    {
        Focusable = true; ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        _animation.Tick += (_, _) =>
        {
            if (Document is null || Document.Frames.Count < 2) return;
            _frame = (_frame + 1) % Document.Frames.Count;
            if (_frame == 0)
            {
                _loops++;
                if (Document.LoopCount > 0 && _loops >= Document.LoopCount) { _frame = Document.Frames.Count - 1; _animation.Stop(); return; }
            }
            _animation.Interval = TimeSpan.FromMilliseconds(Document.FrameDelaysMs[_frame]);
            InvalidateVisual();
        };
        SizeChanged += (_, _) => UpdateViewport();
        MouseWheel += (_, e) =>
        {
            if (Document is null) return;
            var p = e.GetPosition(this); View.ZoomAt(Math.Pow(1.15, e.Delta / 120d), p.X, p.Y); Changed(); e.Handled = true;
        };
        MouseLeftButtonDown += (_, e) =>
        {
            Focus();
            if (e.ClickCount == 2) { FullscreenRequested?.Invoke(); return; }
            _drag = e.GetPosition(this); CaptureMouse(); Cursor = Cursors.SizeAll;
        };
        MouseMove += (_, e) =>
        {
            if (_drag is not Point p || Document is null) return;
            var now = e.GetPosition(this); View.Pan(now.X - p.X, now.Y - p.Y); _drag = now; Changed();
        };
        MouseLeftButtonUp += (_, _) => EndDrag();
        LostMouseCapture += (_, _) => { _drag = null; Cursor = Cursors.Arrow; };
    }
    private void EndDrag() { _drag = null; ReleaseMouseCapture(); Cursor = Cursors.Arrow; }
    public void SetDocument(ImageDocument? doc, ViewportFitMode fit)
    {
        _animation.Stop(); Document = doc; _frame = 0; _loops = 0; Mirrored = false; AnimationPaused = false;
        if (doc is not null)
        {
            View.SetViewport(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight), VisualTreeHelper.GetDpi(this).DpiScaleX);
            View.SetImage(doc.Width, doc.Height); View.Fit(fit);
            if (doc.Frames.Count > 1) { _animation.Interval = TimeSpan.FromMilliseconds(doc.FrameDelaysMs[0]); _animation.Start(); }
        }
        Changed();
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); UpdateViewport(); }
    public void UpdateViewport()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        View.SetViewport(ActualWidth, ActualHeight, VisualTreeHelper.GetDpi(this).DpiScaleX); Changed();
    }
    public void Fit(ViewportFitMode mode) { if (Document is null) return; View.Fit(mode); Changed(); }
    public void Zoom(double factor) { if (Document is null) return; View.ZoomAt(factor, ActualWidth / 2, ActualHeight / 2); Changed(); }
    public void Rotate() { if (Document is null) return; View.RotateClockwise(); View.Fit(View.FitMode); Changed(); }
    public void Mirror() { Mirrored = !Mirrored; Changed(); }
    public void PauseAnimation()
    {
        if (Document is null || Document.Frames.Count < 2) return;
        AnimationPaused = !AnimationPaused;
        if (AnimationPaused) _animation.Stop(); else _animation.Start();
        ViewChanged?.Invoke();
    }
    private void Changed() { InvalidateVisual(); ViewChanged?.Invoke(); }
    protected override void OnRender(DrawingContext dc)
    {
        var canvas = CanvasMode switch { "black" => Brushes.Black, "white" => Brushes.White, _ => CanvasBrush };
        dc.DrawRectangle(canvas, null, new Rect(RenderSize));
        if (Document is null) return;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        double x = View.OffsetX, y = View.OffsetY;
        if (Math.Abs(View.Scale * View.DpiScale - 1) < 0.000001)
        {
            x = Math.Round(x * View.DpiScale) / View.DpiScale;
            y = Math.Round(y * View.DpiScale) / View.DpiScale;
        }
        Brush alphaBrush = CanvasMode switch
        {
            "black" => Brushes.Black,
            "white" => Brushes.White,
            _ => AlphaBackground switch { "light" => Brushes.White, "checker" => Checker, _ => CanvasBrush }
        };
        dc.DrawRectangle(alphaBrush, null, new Rect(x, y, View.DisplayWidth, View.DisplayHeight));
        dc.PushTransform(new TranslateTransform(x, y));
        dc.PushTransform(new ScaleTransform(View.Scale, View.Scale));
        var w = Document.Width; var h = Document.Height;
        var matrix = View.QuarterTurnsClockwise switch
        {
            1 => new Matrix(0, 1, -1, 0, h, 0),
            2 => new Matrix(-1, 0, 0, -1, w, h),
            3 => new Matrix(0, -1, 1, 0, 0, w),
            _ => Matrix.Identity
        };
        dc.PushTransform(new MatrixTransform(matrix));
        if (Mirrored) dc.PushTransform(new MatrixTransform(-1, 0, 0, 1, w, 0));
        var rect = new Rect(0, 0, w, h);
        dc.DrawImage(Document.Frames[_frame], rect);
        if (Mirrored) dc.Pop();
        dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
    }
    private static Brush CreateChecker()
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(185, 188, 192)), null, new Rect(0, 0, 24, 24));
            var light = new SolidColorBrush(Color.FromRgb(222, 224, 227));
            dc.DrawRectangle(light, null, new Rect(0, 0, 12, 12)); dc.DrawRectangle(light, null, new Rect(12, 12, 12, 12));
        }
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 24, 24), Stretch = Stretch.None };
        brush.Freeze(); return brush;
    }
}
