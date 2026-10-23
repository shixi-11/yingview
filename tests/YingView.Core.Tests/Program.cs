using YingView.Core;

static void Near(double actual, double expected, string label, double epsilon = 1e-8)
{
    if (Math.Abs(actual - expected) > epsilon) throw new Exception($"{label}: expected {expected}, got {actual}");
}
static void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
}

// Initialization order is independent: either viewport or image can arrive first.
var imageFirst = new Viewport();
imageFirst.SetImage(400, 200);
imageFirst.SetViewport(800, 600, 1);
Near(imageFirst.DisplayWidth, 800, "image-first initialization fits after viewport arrives");
var viewportFirst = new Viewport();
viewportFirst.SetViewport(800, 600, 1);
viewportFirst.SetImage(400, 200);
Near(viewportFirst.DisplayWidth, 800, "viewport-first initialization fits after image arrives");

var viewport = new Viewport();
viewport.SetViewport(1000, 600, 1);
viewport.SetImage(3840, 2160);
viewport.Fit(ViewportFitMode.Contain);
Near(viewport.Scale, 1000d / 3840, "4K contain scale");
Near(viewport.DisplayWidth, 1000, "4K contain width");
Near(viewport.DisplayHeight, 562.5, "4K contain height");

viewport.SetImage(200, 100);
viewport.Fit(ViewportFitMode.Contain);
Near(viewport.Scale, 5, "small image is enlarged to contain");
Near(viewport.DisplayWidth, 1000, "small image reaches viewport width");

viewport.SetImage(1600, 900);
viewport.Fit(ViewportFitMode.Contain);
Near(viewport.DisplayWidth, 1000, "mixed aspect contain width");
Near(viewport.DisplayHeight, 562.5, "mixed aspect contain preserves whole image");
viewport.Fit(ViewportFitMode.Cover);
Near(viewport.DisplayWidth, 1066.6666666666667, "cover fills width with crop");
Near(viewport.DisplayHeight, 600, "cover fills viewport height");

viewport.SetViewport(1000, 600, 1);
viewport.SetImage(1000, 600);
viewport.Fit(ViewportFitMode.Actual);
viewport.ZoomAt(2, 250, 180);
Near((250 - viewport.OffsetX) / viewport.Scale, 250, "zoom anchor image x");
Near((180 - viewport.OffsetY) / viewport.Scale, 180, "zoom anchor image y");

viewport.Fit(ViewportFitMode.Actual);
viewport.ZoomAt(100000, 500, 300);
Near(viewport.Scale * viewport.DpiScale, 128, "zoom maximum physical scale");
viewport.ZoomAt(0.000001, 500, 300);
Near(viewport.Scale * viewport.DpiScale, 0.01, "zoom minimum physical scale");

viewport.Fit(ViewportFitMode.Actual);
viewport.ZoomAt(3, 500, 300);
var physicalScaleBeforeResize = viewport.Scale * viewport.DpiScale;
viewport.SetViewport(1200, 700, 2);
Near(viewport.Scale * viewport.DpiScale, physicalScaleBeforeResize, "manual physical zoom survives resize and DPI change");

viewport.SetViewport(1000, 600, 2);
viewport.SetImage(2000, 1200);
viewport.Fit(ViewportFitMode.Actual);
Near(viewport.Scale, 0.5, "actual scale at 200% DPI");
Near(viewport.DisplayWidth * 2, 2000, "one image pixel per physical display pixel");

viewport.SetViewport(1000, 600, 1);
viewport.SetImage(2000, 1200);
viewport.Fit(ViewportFitMode.Actual);
viewport.Pan(10000, -10000);
Near(viewport.OffsetX, 0, "pan right clamped");
Near(viewport.OffsetY, -600, "pan up clamped");
viewport.Pan(-10000, 10000);
Near(viewport.OffsetX, -1000, "pan left clamped");
Near(viewport.OffsetY, 0, "pan down clamped");

viewport.SetImage(800, 400);
viewport.Fit(ViewportFitMode.Actual);
var previousScale = viewport.Scale;
viewport.RotateClockwise();
Near(viewport.DisplayWidth, 400 * previousScale, "rotation swaps rendered width");
Near(viewport.DisplayHeight, 800 * previousScale, "rotation swaps rendered height");
Check(viewport.QuarterTurnsClockwise == 1, "rotation count advances");

var names = new[] { "photo10.png", "photo2.png", "photo999999999999999999999999999999.png", "photo1000000000000000000000000000000.png", "photo02.png", "photo2.PNG" };
Array.Sort(names, NaturalStringComparer.Ordinal);
Check(Array.IndexOf(names, "photo2.PNG") < Array.IndexOf(names, "photo2.png"), "equal numeric runs use deterministic ordinal fallback");
Check(Array.IndexOf(names, "photo02.png") < Array.IndexOf(names, "photo2.png"), "leading zero tie uses ordinal fallback");
Check(Array.IndexOf(names, "photo999999999999999999999999999999.png") < Array.IndexOf(names, "photo1000000000000000000000000000000.png"), "huge digit runs sort numerically without overflow");

Console.WriteLine("All YingView.Core assertions passed.");
