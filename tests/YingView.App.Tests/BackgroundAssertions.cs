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
        var originalCustom = settings.CustomBackgroundColor;
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

        Check(CanvasColor.TryNormalize(" ab12ef ", out var normalized) && normalized == "#AB12EF", "HEX entry must normalize optional #, case and whitespace.");
        foreach (var invalid in new[] { "", "##123456", "#12345G", "#00123456", "Red", "#123456\nX" })
            Check(!CanvasColor.TryNormalize(invalid, out _), "Invalid or transparent color values must be rejected.");
        static T Find<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match && match.Name == name) return match;
                try { return Find<T>(child, name); } catch (InvalidOperationException) { }
            }
            throw new InvalidOperationException("Missing color picker control: " + name);
        }
        var choose = typeof(MainWindow).GetMethod("ChooseCanvasColor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pickerField = typeof(MainWindow).GetField("_canvasColorPicker", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var paletteHost = (System.Windows.Controls.Border)typeof(MainWindow).GetField("CanvasColorHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var root = (FrameworkElement)window.Content;
        var settingsFile = (string)typeof(Preferences).GetProperty("FileName", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        var originalDocument = surface.Document;
        var layoutBitmap = BitmapSource.Create(20, 10, 96, 96, PixelFormats.Bgra32, null, new byte[800], 80); layoutBitmap.Freeze();
        using var layoutDocument = new ImageDocument([layoutBitmap], [100], 20, 10, "PNG", 800);
        surface.SetDocument(layoutDocument, ViewportFitMode.Contain);
        var windowCount = Application.Current.Windows.Count;
        var keyboardSource = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle())!;
        void Layout(double width, double height)
        {
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        }
        CanvasColorPicker OpenPicker()
        {
            choose.Invoke(window, null); Layout(1280, 800);
            Check(Application.Current.Windows.Count == windowCount, "The palette must be embedded without opening an extra window.");
            return (CanvasColorPicker)pickerField.GetValue(window)!;
        }
        foreach (var mode in new[] { "black", "white", "gray", "checker", "theme", "custom" })
        {
            setBackground.Invoke(window, [mode]);
            var originalBrush = surface.CanvasBackgroundBrush;
            var originalColor = settings.CustomBackgroundColor;
            var savedBeforePreview = File.ReadAllBytes(settingsFile);
            var picker = OpenPicker();
            Check(paletteHost.Visibility == Visibility.Visible, "Opening the palette must show the embedded panel.");
            var hex = Find<System.Windows.Controls.TextBox>(picker, "ColorHex");
            var apply = Find<System.Windows.Controls.Button>(picker, "ApplyColor");
            hex.Text = "#1234AB";
            Check(surface.CanvasMode == "custom" && ((SolidColorBrush)surface.CanvasBackgroundBrush).Color == Color.FromRgb(18, 52, 171),
                "Valid color edits must preview on the actual image canvas before Apply.");
            hex.Text = "not-a-color";
            Check(!apply.IsEnabled && ((SolidColorBrush)surface.CanvasBackgroundBrush).Color == Color.FromRgb(18, 52, 171), "Invalid HEX must disable Apply and retain the last valid preview.");
            Find<System.Windows.Controls.Primitives.UniformGrid>(picker, "ColorSwatches").Children.OfType<System.Windows.Controls.Button>().First()
                .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(((SolidColorBrush)surface.CanvasBackgroundBrush).Color == Colors.White, "Swatches must preview immediately.");
            hex.Text = "#FF0000";
            Find<System.Windows.Controls.Slider>(picker, "ColorHue").Value = 120;
            Check(((SolidColorBrush)surface.CanvasBackgroundBrush).Color == Colors.Lime, "Dragging hue must preview without Apply.");
            var field = Find<FrameworkElement>(picker, "ColorArea");
            void Key(System.Windows.Input.Key key) => field.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                keyboardSource, 0, key) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
            Key(System.Windows.Input.Key.Left);
            Check(hex.Text == "#03FF03", "The palette must support keyboard saturation adjustment.");
            Key(System.Windows.Input.Key.Right); Key(System.Windows.Input.Key.Right); Key(System.Windows.Input.Key.Up); Key(System.Windows.Input.Key.Down);
            Check(hex.Text == "#00FC00", "Palette keyboard movement must clamp boundaries and adjust brightness.");
            Check(settings.Background == mode && settings.CustomBackgroundColor == originalColor, "Preview must not mutate committed preferences.");
            Check(savedBeforePreview.SequenceEqual(File.ReadAllBytes(settingsFile)), "Dragging a color must not save the preview.");
            foreach (var size in new[] { new Size(720, 520), new Size(1280, 800), new Size(3840, 2160) })
            {
                Layout(size.Width, size.Height);
                var imageBounds = new Rect(surface.TranslatePoint(new Point(), root), surface.RenderSize);
                var paletteBounds = new Rect(paletteHost.TranslatePoint(new Point(), root), paletteHost.RenderSize);
                Check(imageBounds.Right <= paletteBounds.Left + .5 && imageBounds.Width >= 400,
                    "The palette must sit beside the image without covering its canvas, even at minimum window size.");
                Check(surface.View.DisplayWidth <= surface.ActualWidth + .5 && surface.View.DisplayHeight <= surface.ActualHeight + .5,
                    "The whole image must still fit while the embedded palette is open.");
                Check(paletteBounds.Right <= root.ActualWidth + .5 && paletteBounds.Bottom <= root.ActualHeight + .5,
                    "The palette must remain inside the viewer at all supported window sizes.");
            }
            var cancel = Find<System.Windows.Controls.Button>(picker, mode is "black" or "checker" or "custom" ? "CancelColor" : "ClosePalette");
            cancel.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(pickerField.GetValue(window) is null && paletteHost.Visibility == Visibility.Collapsed, "Cancel or close must collapse the embedded palette.");
            Check(settings.Background == mode && settings.CustomBackgroundColor == originalColor, "Cancel must preserve the original committed color.");
            Check(Equals(surface.CanvasBackgroundBrush, originalBrush) || surface.CanvasBackgroundBrush.ToString() == originalBrush.ToString(), "Cancel must restore the exact original canvas background.");
            Check(savedBeforePreview.SequenceEqual(File.ReadAllBytes(settingsFile)), "Cancel must not save draft preferences.");
            Check(button.ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>().Count(i => i.IsChecked) == 1, "Cancel must retain one checked background choice.");
        }
        var acceptedPicker = OpenPicker();
        Find<System.Windows.Controls.TextBox>(acceptedPicker, "ColorHex").Text = "#365A79";
        Find<System.Windows.Controls.Button>(acceptedPicker, "ApplyColor").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var committed = Preferences.Load(settingsFile);
        Check(committed.Background == "custom" && committed.CustomBackgroundColor == "#365A79", "Apply must save the final color.");
        Check(pickerField.GetValue(window) is null && paletteHost.Visibility == Visibility.Collapsed, "Apply must close the embedded palette.");
        Check(((SolidColorBrush)surface.CanvasBackgroundBrush).Color == Color.FromRgb(54, 90, 121), "Apply must retain the selected background.");
        OpenPicker();
        var keyHandler = typeof(MainWindow).GetMethod("KeyPressed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, keyboardSource, 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
        keyHandler.Invoke(window, [window, escape]);
        Check(escape.Handled && pickerField.GetValue(window) is null, "Escape must cancel the embedded palette before other viewer shortcuts.");
        OpenPicker(); setBackground.Invoke(window, ["black"]);
        Check(pickerField.GetValue(window) is null && settings.Background == "black", "Selecting a preset must close and discard any palette draft.");
        Layout(1280, 800);
        surface.SetDocument(originalDocument, ViewportFitMode.Contain);
        var customSettings = new Preferences(path) { Background = "custom", CustomBackgroundColor = "#19A7C2", AppearanceUiVersion = 2 };
        Check(customSettings.Save(), "Custom color settings must save.");
        var reloaded = Preferences.Load(path);
        Check(reloaded.Background == "custom" && reloaded.CustomBackgroundColor == "#19A7C2", "Restart must restore the custom mode and exact color.");
        customSettings.CustomBackgroundColor = "corrupt"; customSettings.Save();
        Check(Preferences.Load(path).CustomBackgroundColor == "#D6E4EF", "Malformed stored colors must recover safely.");

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
        renderSurface.CanvasMode = "custom"; renderSurface.CustomCanvasBrush = ViewerTheme.Brush("#19A7C2"); renderSurface.InvalidateVisual();
        var customFrame = Render(renderSurface, 120, 80);
        Save(customFrame, Path.Combine(outputDirectory, "canvas-custom.png"));
        Check(new[] { Read(customFrame, 0, 0), Read(customFrame, 60, 40), Read(customFrame, 119, 79) }.All(c => c == Color.FromRgb(25, 167, 194)),
            "Custom color must fill the outer canvas and transparent PNG pixels with the exact same color.");
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
        settings.CustomBackgroundColor = originalCustom;
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
