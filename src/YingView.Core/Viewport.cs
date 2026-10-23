namespace YingView.Core;

/// <summary>Controls how the complete image maps into the viewport.</summary>
public enum ViewportFitMode
{
    /// <summary>Show the whole image, centered. Small images are enlarged to fill as much as possible.</summary>
    Contain,
    /// <summary>Fill the viewport while preserving aspect ratio; the image may extend beyond its edges.</summary>
    Cover,
    /// <summary>Map one source pixel to one physical display pixel, accounting for the monitor DPI scale.</summary>
    Actual
}

/// <summary>
/// Pure geometry for an image viewer. Coordinates and offsets are device-independent pixels (DIP);
/// image dimensions are source pixels. Offset is the top-left of the rendered image in viewport coordinates.
/// </summary>
public sealed class Viewport
{
    private double _viewportWidth;
    private double _viewportHeight;
    private double _dpiScale = 1;
    private double _imageWidth;
    private double _imageHeight;
    private int _quarterTurns;
    private ViewportFitMode _mode = ViewportFitMode.Contain;
    private bool _hasManualZoom;

    private const double MinPhysicalScale = 0.01;
    private const double MaxPhysicalScale = 128;

    public double Scale { get; private set; } = 1;
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }
    public double DisplayWidth => OrientedWidth * Scale;
    public double DisplayHeight => OrientedHeight * Scale;
    public double ViewportWidth => _viewportWidth;
    public double ViewportHeight => _viewportHeight;
    public double DpiScale => _dpiScale;
    public double ImageWidth => _imageWidth;
    public double ImageHeight => _imageHeight;
    public int QuarterTurnsClockwise => _quarterTurns;
    public ViewportFitMode FitMode => _mode;
    private double OrientedWidth => (_quarterTurns % 2 == 0) ? _imageWidth : _imageHeight;
    private double OrientedHeight => (_quarterTurns % 2 == 0) ? _imageHeight : _imageWidth;

    public void SetViewport(double widthDip, double heightDip, double dpiScale)
    {
        ValidatePositiveFinite(widthDip, nameof(widthDip));
        ValidatePositiveFinite(heightDip, nameof(heightDip));
        ValidatePositiveFinite(dpiScale, nameof(dpiScale));
        var hadViewport = _viewportWidth > 0 && _viewportHeight > 0;
        var oldDpiScale = _dpiScale;
        _viewportWidth = widthDip;
        _viewportHeight = heightDip;
        _dpiScale = dpiScale;
        if (!IsReady) return;

        if (_hasManualZoom && hadViewport)
        {
            // Keep the same physical magnification when the viewport or monitor DPI changes.
            Scale = ClampPhysicalScale(Scale * oldDpiScale) / _dpiScale;
            OffsetX = (_viewportWidth - DisplayWidth) / 2;
            OffsetY = (_viewportHeight - DisplayHeight) / 2;
            ClampPan();
        }
        else
        {
            ApplyFit(_mode);
        }
    }

    public void SetImage(double widthPx, double heightPx)
    {
        ValidatePositiveFinite(widthPx, nameof(widthPx));
        ValidatePositiveFinite(heightPx, nameof(heightPx));
        _imageWidth = widthPx;
        _imageHeight = heightPx;
        _quarterTurns = 0;
        _hasManualZoom = false;
        if (IsReady) ApplyFit(_mode);
    }

    public void Fit(ViewportFitMode mode)
    {
        EnsureReady();
        _mode = mode;
        _hasManualZoom = false;
        ApplyFit(mode);
    }

    private void ApplyFit(ViewportFitMode mode)
    {
        Scale = mode switch
        {
            ViewportFitMode.Contain => Math.Min(_viewportWidth / OrientedWidth, _viewportHeight / OrientedHeight),
            ViewportFitMode.Cover => Math.Max(_viewportWidth / OrientedWidth, _viewportHeight / OrientedHeight),
            ViewportFitMode.Actual => 1d / _dpiScale,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        OffsetX = (_viewportWidth - DisplayWidth) / 2;
        OffsetY = (_viewportHeight - DisplayHeight) / 2;
        ClampPan();
    }

    /// <summary>Zooms around a viewport-space DIP point; the source point under the pointer stays fixed until clamping.</summary>
    public void ZoomAt(double factor, double xDip, double yDip)
    {
        EnsureReady();
        ValidatePositiveFinite(factor, nameof(factor));
        ValidateFinite(xDip, nameof(xDip));
        ValidateFinite(yDip, nameof(yDip));
        var imageX = (xDip - OffsetX) / Scale;
        var imageY = (yDip - OffsetY) / Scale;
        Scale = ClampPhysicalScale(Scale * factor * _dpiScale) / _dpiScale;
        OffsetX = xDip - imageX * Scale;
        OffsetY = yDip - imageY * Scale;
        _hasManualZoom = true;
        ClampPan();
    }

    /// <summary>Moves the rendered image by DIP deltas. Images smaller than the viewport remain centered on that axis.</summary>
    public void Pan(double dxDip, double dyDip)
    {
        EnsureReady();
        ValidateFinite(dxDip, nameof(dxDip));
        ValidateFinite(dyDip, nameof(dyDip));
        OffsetX += dxDip;
        OffsetY += dyDip;
        ClampPan();
    }

    /// <summary>Rotates clockwise by 90 degrees around the viewport center, then clamps the resulting image.</summary>
    public void RotateClockwise()
    {
        EnsureReady();
        _quarterTurns = (_quarterTurns + 1) % 4;
        // Preserve scale and viewport center, while the oriented image dimensions swap.
        OffsetX = (_viewportWidth - DisplayWidth) / 2;
        OffsetY = (_viewportHeight - DisplayHeight) / 2;
        ClampPan();
    }

    private void ClampPan()
    {
        OffsetX = ClampAxis(OffsetX, DisplayWidth, _viewportWidth);
        OffsetY = ClampAxis(OffsetY, DisplayHeight, _viewportHeight);
    }

    private static double ClampAxis(double offset, double display, double viewport)
    {
        if (display <= viewport) return (viewport - display) / 2;
        return Math.Clamp(offset, viewport - display, 0);
    }

    private void EnsureReady()
    {
        if (_viewportWidth <= 0 || _viewportHeight <= 0) throw new InvalidOperationException("Set a viewport before using image geometry.");
        if (_imageWidth <= 0 || _imageHeight <= 0) throw new InvalidOperationException("Set an image before using image geometry.");
    }

    private bool IsReady => _viewportWidth > 0 && _viewportHeight > 0 && _imageWidth > 0 && _imageHeight > 0;

    private static double ClampPhysicalScale(double physicalScale) =>
        Math.Clamp(physicalScale, MinPhysicalScale, MaxPhysicalScale);

    private static void ValidatePositiveFinite(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name, "Value must be finite and greater than zero.");
    }
    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(name, "Value must be finite.");
    }
}
