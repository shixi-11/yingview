using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace YingView;

internal sealed class PaletteIcon : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(PaletteIcon), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    private static readonly Geometry Outline = Geometry.Parse("M12,3 C6.9,3 3,6.8 3,11.5 C3,16.6 6.8,21 11.2,21 C12.8,21 13.5,20 13.5,18.8 C13.5,17.8 12.8,17.4 12.8,16.5 C12.8,15.6 13.4,15 14.3,15 L16.7,15 C19.2,15 21,13.2 21,10.8 C21,6.2 17,3 12,3 Z");
    private static readonly Brush Blue = ViewerTheme.Brush("#60A5FA");
    private static readonly Brush Green = ViewerTheme.Brush("#5FC5A2");
    private static readonly Brush Orange = ViewerTheme.Brush("#F4B15F");
    static PaletteIcon() => Outline.Freeze();
    public PaletteIcon()
    {
        Width = Height = 18;
        IsHitTestVisible = false;
        SetResourceReference(ForegroundProperty, "MutedBrush");
    }
    protected override void OnRender(DrawingContext dc)
    {
        var scale = Math.Min(ActualWidth, ActualHeight) / 24;
        if (scale <= 0) return;
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, (ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        dc.DrawGeometry(null, new Pen(Foreground, 1.6) { LineJoin = PenLineJoin.Round }, Outline);
        dc.DrawEllipse(Blue, null, new Point(6.8, 10.3), 1.45, 1.45);
        dc.DrawEllipse(Green, null, new Point(10, 7), 1.45, 1.45);
        dc.DrawEllipse(Orange, null, new Point(14.4, 7.1), 1.45, 1.45);
        dc.DrawEllipse(Foreground, null, new Point(17.5, 10.4), 1.05, 1.05);
        dc.Pop();
    }
}
