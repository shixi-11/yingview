using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YingView.Core;
using YingView.Imaging;

namespace YingView.AppTests;

/// <summary>Focused assertions for the canvas background behavior; invoke from the app test harness.</summary>
internal static class BackgroundAssertions
{
    public static void Run(MainWindow window, string outputDirectory)
    {
        Check(!System.Windows.Input.InputMethod.GetIsInputMethodEnabled(window), "The non-editable viewer must not send background shortcuts into a Chinese IME.");
        Directory.CreateDirectory(outputDirectory);
        var settings = (Preferences)typeof(MainWindow).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var originalBackground = settings.Background;
        var setBackground = typeof(MainWindow).GetMethod("SetCanvasBackground", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var surface = (ImageSurface)typeof(MainWindow).GetField("Surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

        setBackground.Invoke(window, ["black"]);
        Check(settings.Background == "black" && surface.CanvasMode == "black", "Black mode must update the full canvas and preference.");
        var title = (System.Windows.Controls.TextBlock)typeof(MainWindow).GetField("EmptyTitle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Check(((SolidColorBrush)title.Foreground).Color == Colors.White, "Empty-state title must stay readable on black.");
        setBackground.Invoke(window, ["white"]);
        Check(settings.Background == "white" && surface.CanvasMode == "white", "White mode must update the full canvas and preference.");

        setBackground.Invoke(window, ["black"]);
        var button = (System.Windows.Controls.Button)typeof(MainWindow).GetField("CanvasToggleButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        foreach (var expected in new[] { "white", "gray", "checker", "black" })
        {
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(settings.Background == expected && surface.CanvasMode == expected, "One click must cycle through all four backgrounds.");
            var checkedItems = button.ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>().Where(i => i.IsChecked).ToArray();
            Check(checkedItems.Length == 1, "The direct background menu must indicate exactly one selected color.");
        }
        var grayChoice = button.ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>().Single(i => Equals(i.Header, "Gray"));
        grayChoice.ApplyTemplate();
        var menuColor = ((SolidColorBrush)button.ContextMenu.Background).Color;
        var textColor = ((SolidColorBrush)grayChoice.Foreground).Color;
        static double Luminance(Color color)
        {
            static double Channel(byte v) { var n = v / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        var menuL = Luminance(menuColor); var textL = Luminance(textColor);
        Check((Math.Max(menuL, textL) + .05) / (Math.Min(menuL, textL) + .05) >= 4.5,
            "Background menu text must remain readable against its popup background.");
        grayChoice.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        Check(settings.Background == "gray", "A direct background choice must apply immediately.");

        var path = Path.Combine(outputDirectory, "canvas-background-settings.json");
        var isolatedSettings = new Preferences(path) { Background = "gray" };
        Check(isolatedSettings.Save(), "A selected canvas color must save to the isolated test preferences file.");
        using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        Check(saved.RootElement.GetProperty("Background").GetString() == "gray", "Selected canvas color must persist.");

        var transparent = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        transparent.Freeze();
        var originalPixels = new byte[16]; transparent.CopyPixels(originalPixels, 8, 0);
        var renderSurface = new ImageSurface { Width = 120, Height = 80, CanvasBrush = Brushes.Gray, CanvasMode = "black", AlphaBackground = "dark" };
        Check(!System.Windows.Input.InputMethod.GetIsInputMethodEnabled(renderSurface), "An image canvas must not start text composition.");
        using var host = new HwndSource(new HwndSourceParameters("YingView.BackgroundAssertions") { Width = 120, Height = 80, WindowStyle = 0 });
        host.RootVisual = renderSurface;
        renderSurface.Measure(new Size(120, 80)); renderSurface.Arrange(new Rect(0, 0, 120, 80));
        var doc = new ImageDocument([transparent], [100], 2, 2, "PNG", 16);
        renderSurface.SetDocument(doc, ViewportFitMode.Actual);
        var blackFrame = Render(renderSurface, 120, 80);
        Save(blackFrame, Path.Combine(outputDirectory, "canvas-black.png"));
        Check(IsBlack(Read(blackFrame, 0, 0)) && IsBlack(Read(blackFrame, 119, 79)) && IsBlack(Read(blackFrame, 60, 40)), "Black must fill the area outside the image and transparent pixels.");
        renderSurface.CanvasMode = "white"; renderSurface.InvalidateVisual();
        var whiteFrame = Render(renderSurface, 120, 80);
        Save(whiteFrame, Path.Combine(outputDirectory, "canvas-white.png"));
        Check(IsWhite(Read(whiteFrame, 0, 0)) && IsWhite(Read(whiteFrame, 119, 79)) && IsWhite(Read(whiteFrame, 60, 40)), "White must fill the area outside the image and transparent pixels.");
        renderSurface.CanvasMode = "gray"; renderSurface.InvalidateVisual();
        var grayFrame = Render(renderSurface, 120, 80);
        Save(grayFrame, Path.Combine(outputDirectory, "canvas-gray.png"));
        Check(Read(grayFrame, 0, 0).R == 128 && Read(grayFrame, 60, 40).R == 128 && Read(grayFrame, 119, 79).R == 128,
            "Gray must cover both the outer canvas and transparent PNG pixels.");
        renderSurface.CanvasMode = "checker"; renderSurface.InvalidateVisual();
        var checkerFrame = Render(renderSurface, 120, 80);
        Save(checkerFrame, Path.Combine(outputDirectory, "canvas-checker.png"));
        Check(Read(checkerFrame, 2, 2) != Read(checkerFrame, 14, 2), "Checkerboard must also fill the canvas outside the image.");
        renderSurface.SetDocument(null, ViewportFitMode.Actual);
        var emptyChecker = Render(renderSurface, 120, 80);
        Check(Read(checkerFrame, 60, 40) == Read(emptyChecker, 60, 40), "Checkerboard must remain aligned through transparent pixels without a second image-sized background.");

        var afterPixels = new byte[16]; transparent.CopyPixels(afterPixels, 8, 0);
        Check(originalPixels.SequenceEqual(afterPixels), "Changing the canvas must not modify image RGB or alpha data.");
        renderSurface.SetDocument(null, ViewportFitMode.Contain);
        host.RootVisual = null;
        setBackground.Invoke(window, [originalBackground]);
    }

    private static RenderTargetBitmap Render(FrameworkElement element, int width, int height)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element); return bitmap;
    }
    private static Color Read(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
    private static bool IsBlack(Color color) => color.R < 8 && color.G < 8 && color.B < 8;
    private static bool IsWhite(Color color) => color.R > 247 && color.G > 247 && color.B > 247;
    private static void Save(BitmapSource bitmap, string path)
    {
        using var stream = File.Create(path); new PngBitmapEncoder { Frames = { BitmapFrame.Create(bitmap) } }.Save(stream);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
