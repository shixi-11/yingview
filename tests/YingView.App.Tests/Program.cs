using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using YingView;
using YingView.Core;
using YingView.Review;

internal static class Program
{
    private static readonly string Output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "output", "app-tests"));

    [STAThread]
    private static int Main()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Check(ClientBranding.ResolveLanguage("zh-CN", "en-US") == "zh", "Chinese download must retain Chinese branding.");
            Check(ClientBranding.ResolveLanguage("en-US", "zh-CN") == "en", "English download must retain English branding.");
            Check(ClientBranding.ResolveLanguage("auto", "ja-JP") == "en", "Other locales must fall back to English.");
            Check(ClientBranding.DisplayName("zh") == "映窗" && ClientBranding.DisplayName("en") == "YingView", "Display names must stay localized.");
            Console.WriteLine("Client branding assertions passed. SKIP: WPF offscreen rendering and Recycle Bin tests require a local Windows desktop session.");
            return 0;
        }
        Directory.CreateDirectory(Output);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        File.WriteAllText(Path.Combine(Output, "result.txt"), "RUNNING\n");
        var settingsPath = Path.Combine(Output, "settings", "settings.json");
        var settings = new Preferences(settingsPath) { Language = "en", Filmstrip = false, AppearanceUiVersion = 2 };
        var window = new MainWindow(settings);
        try
        {
            VerifyClientLanguageBranding(window, settings);
            VerifyLayout(window);
            var fixtures = CreateFixtures();
            try
            {
                Pump(InvokeAsync(window, "OpenPaths", (object)new[] { fixtures[0], fixtures[1], fixtures[2] }));
                VerifyReviewWindow(fixtures);
                var surface = (FrameworkElement)Field(window, "Surface");
                Check(surface.ActualWidth > 0 && surface.ActualHeight > 0, "Detached surface must be laid out.");
                var view = (Viewport)Property(surface, "View");
                Check(view.FitMode == ViewportFitMode.Contain && view.DisplayWidth <= view.ViewportWidth + 1 && view.DisplayHeight <= view.ViewportHeight + 1, "A small image should begin fully contained.");
                Pump(InvokeAsync(window, "Navigate", 1));
                Check(view.FitMode == ViewportFitMode.Contain && view.DisplayWidth <= view.ViewportWidth + 1 && view.DisplayHeight <= view.ViewportHeight + 1, "Navigating to a portrait image should return to contain fit.");
                Pump(InvokeAsync(window, "Navigate", 1));
                Check(view.FitMode == ViewportFitMode.Contain && view.DisplayWidth <= view.ViewportWidth + 1 && view.DisplayHeight <= view.ViewportHeight + 1, "Navigating to a large image should return to contain fit.");

                var root = (Grid)Field(window, "Root");
                window.Width = 1280; window.Height = 800;
                SaveReviewPng(window, root, "main-1280-en.png");
                var originalTheme = settings.Theme; var originalLanguage = settings.Language;
                settings.Theme = ViewerTheme.BuiltIn[1]; settings.Language = "zh";
                InvokeVoid(window, "ApplyTheme", settings.Theme); InvokeVoid(window, "ApplyLanguage");
                window.Width = 720; window.Height = 1280;
                SaveReviewPng(window, root, "main-720-zh-paper.png");
                settings.Theme = originalTheme; settings.Language = originalLanguage;
                InvokeVoid(window, "ApplyTheme", settings.Theme); InvokeVoid(window, "ApplyLanguage");
                var filmstripButton = (Button)Field(window, "FilmstripToggleButton");
                filmstripButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(root.RowDefinitions[2].Height.Value == 62, "Filmstrip button should expand the 62px row.");
                filmstripButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(root.RowDefinitions[2].Height.Value == 0, "Filmstrip button should collapse its row.");
                VerifyDeleteKeyWithoutSelection(window);
                if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine("SKIP: Recycle Bin integration is skipped because CI=true.");
                else if (!string.Equals(Environment.GetEnvironmentVariable("YINGVIEW_TEST_ALLOW_SHELL_CONFIRMATION"), "true", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine("SKIP: Recycle Bin integration requires YINGVIEW_TEST_ALLOW_SHELL_CONFIRMATION=true because Windows may display a Shell confirmation dialog.");
                else
                {
                    VerifyDeleteKeyAndRecycle(window, fixtures[0], fixtures[1]);
                    VerifyDeleteKeyAndRecycle(window, fixtures[1], fixtures[2]);
                    VerifyDeleteKeyAndRecycle(window, fixtures[2], null);
                    Check(!File.Exists(fixtures[0]) && !File.Exists(fixtures[1]) && !File.Exists(fixtures[2]), "Delete flow should recycle each disposable fixture.");
                }
                VerifyStyleCancel(settings);
                // The representative UI captures above are rendered before any delete operation.
            }
            finally
            {
                // Keep the empty fixture directory as a visible record; the files themselves are recycled by the test.
            }
            Console.WriteLine("WPF app integration assertions passed. Fixtures, isolated settings, and any render artifacts are under output/app-tests.");
            File.WriteAllText(Path.Combine(Output, "result.txt"), "PASS\n");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            File.WriteAllText(Path.Combine(Output, "result.txt"), "FAIL\n" + ex);
            return 1;
        }
    }

    private static void VerifyClientLanguageBranding(MainWindow englishWindow, Preferences englishSettings)
    {
        Check(ClientBranding.ResolveLanguage("zh-CN", "en-US") == "zh", "zh-CN distribution must select Chinese regardless of system language.");
        Check(ClientBranding.ResolveLanguage("en-US", "zh-CN") == "en", "en-US distribution must select English regardless of system language.");
        Check(ClientBranding.ResolveLanguage("auto", "ja-JP") == "en" && ClientBranding.ResolveLanguage("auto", "fr-FR") == "en", "Unsupported auto-detected system languages should fall back to English.");
        Check(ClientBranding.ResolveLanguage("auto", "zh-CN") == "zh", "Auto distribution should select Chinese on a Chinese system.");
        Check(ClientBranding.ResolveLanguage("auto", "zh-TW") == "en", "Untranslated Store locales should use the English fallback consistently.");
        Check(ClientBranding.DisplayName("zh") == "映窗" && ClientBranding.DisplayName("en") == "YingView", "Localized display names should remain single-name values.");
        Check(englishWindow.Title == "YingView", "English main window title should use only the English brand.");
        CheckIconResource(englishWindow, "yingview-en-US.ico");

        var chinese = new MainWindow(new Preferences(Path.Combine(Output, "settings", "branding-zh.json")) { Language = "zh", AppearanceUiVersion = 2 });
        Check(chinese.Title == "映窗", "Chinese main window title should use only the Chinese brand.");
        CheckIconResource(chinese, "yingview-zh-CN.ico");
    }

    private static void CheckIconResource(MainWindow window, string expected)
    {
        Check(window.Icon?.ToString().EndsWith(expected, StringComparison.OrdinalIgnoreCase) == true,
            $"Window icon should reference the language-specific resource {expected}.");
    }

    private static string[] CreateFixtures()
    {
        var dir = Path.Combine(Output, "fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new[]
        {
            WritePng(dir, "a-small", 240, 160),
            WritePng(dir, "b-portrait", 240, 420),
            WritePng(dir, "c-large", 4200, 2800)
        };
    }

    private static string WritePng(string dir, string name, int width, int height)
    {
        var bitmap = new WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 0x80; pixels[i + 1] = 0xA0; pixels[i + 2] = 0xC0; pixels[i + 3] = 0xFF; }
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        var path = Path.Combine(dir, name + "-" + Guid.NewGuid().ToString("N") + ".png");
        using var stream = File.Create(path);
        new PngBitmapEncoder { Frames = { BitmapFrame.Create(bitmap) } }.Save(stream);
        return path;
    }

    private static void VerifyLayout(MainWindow window)
    {
        var root = (FrameworkElement)Field(window, "Root");
        foreach (var width in new[] { 720d, 1280d, 3840d })
        {
            window.Width = width;
            window.Height = width == 720 ? 1280 : width == 3840 ? 2160 : 800;
            root.Measure(new Size(window.Width, window.Height));
            root.Arrange(new Rect(0, 0, window.Width, window.Height));
            Check(!double.IsNaN(root.DesiredSize.Width) && root.DesiredSize.Width <= width + 1, $"Layout must fit logical width {width}.");
            Check(root.ActualWidth <= width + 1, $"Root must not overflow logical width {width}.");
        }
    }

    private static void VerifyDeleteKeyAndRecycle(MainWindow window, string target, string? next)
    {
        Pump(InvokeAsync(window, "OpenPaths", (object)new[] { target }));
        Check(string.Equals((string?)Field(window, "_current"), target, StringComparison.OrdinalIgnoreCase), "Delete test must first select its fixture through the app.");
        var before = QueryRecycleBin(Path.GetPathRoot(target)!);
        using var source = new HwndSource(new HwndSourceParameters("YingView.App.Tests.HiddenInputSource") { Width = 1, Height = 1, WindowStyle = 0 });
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Delete)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = window };
        window.RaiseEvent(args);
        Check((bool)Field(window, "_deleting"), "The first Delete key should begin the in-flight recycle operation.");
        var duplicate = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Delete)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = window };
        window.RaiseEvent(duplicate);
        Pump(Dispatcher.CurrentDispatcher.InvokeAsync(() => { }).Task);
        SpinUntil(() => !File.Exists(target) && !(bool)Field(window, "_deleting") && !(bool)Field(window, "_loading"), TimeSpan.FromSeconds(30));
        Check(!File.Exists(target), "Delete key should remove the selected fixture from its source path.");
        Check(QueryRecycleBin(Path.GetPathRoot(target)!).Items >= before.Items + 1, "Delete key should add the fixture to the Windows Recycle Bin.");
        if (next is not null)
            Check(string.Equals((string?)Field(window, "_current"), next, StringComparison.OrdinalIgnoreCase), "Deleting an image should automatically select the next image.");
        else
            Check((string?)Field(window, "_current") is null, "Deleting the last image should clear the viewer.");
    }

    private static void VerifyDeleteKeyWithoutSelection(MainWindow window)
    {
        var current = Field(window, "_current");
        try
        {
            typeof(MainWindow).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
            using var source = new HwndSource(new HwndSourceParameters("YingView.App.Tests.HiddenKeySource") { Width = 1, Height = 1, WindowStyle = 0 });
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Delete)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = window };
            window.RaiseEvent(args);
            Check(args.Handled, "Delete key should be recognized by the app's preview-key handler.");
        }
        finally { typeof(MainWindow).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, current); }
    }

    private static void VerifyStyleCancel(Preferences settings)
    {
        var original = settings.Theme;
        var scalePreviews = new List<double>();
        var studio = new StyleStudioWindow(settings, _ => { }, scale => scalePreviews.Add(scale), _ => { }, true);
        var cancel = FindButton(studio, "Cancel");
        Check(cancel is not null, "Style studio should expose its cancel action.");
        var slider = (Slider)Field(studio, "_scale");
        slider.Value = 1.2;
        SaveWindowContentReviewPng(studio, "style-studio-detached.png", 680, 560);
        cancel!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(settings.Theme == original, "Cancel/close should preserve the original theme.");
        Check(scalePreviews.Count >= 2 && Math.Abs(scalePreviews[^1] - settings.TextScale) < 0.001, "Cancel should restore the live text-scale preview.");
    }

    private static void VerifyReviewWindow(string[] fixtures)
    {
        var review = new ReviewWindow(fixtures, fixtures[0], isZh: false);
        var content = (FrameworkElement)review.Content;
        Pump(InvokeAsync(review, "LoadPairAsync", 0, 1));
        SaveWindowContentReviewPng(review, "review-window-detached.png", 1120, 760);
        var surfaces = (ImageSurface[])Field(review, "_surfaces");
        Check(surfaces.All(s => s.Document is not null), "Review window should load both disposable fixtures.");
        foreach (var surface in surfaces)
            Check(surface.View.FitMode == ViewportFitMode.Contain && surface.View.DisplayWidth <= surface.View.ViewportWidth + 1 && surface.View.DisplayHeight <= surface.View.ViewportHeight + 1,
                "Both review images should initially fit completely inside their panes.");

        surfaces[0].Zoom(2);
        Check(Math.Abs(ZoomRatio(surfaces[0]) - 2) < 0.02 && Math.Abs(ZoomRatio(surfaces[1]) - 2) < 0.02,
            "A real ViewChanged zoom should synchronize relative zoom across images with different contain scales.");
        typeof(ReviewWindow).GetMethod("FitBoth", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(review, null);
        Check(surfaces.All(s => s.View.FitMode == ViewportFitMode.Contain && !s.View.HasManualZoom), "Fit both should restore complete contain view and clear manual zoom on both sides.");

        var syncButton = FindButton(content, "Sync view: On");
        Check(syncButton is not null, "Review window should expose synchronized view control.");
        syncButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!(bool)Field(review, "_syncing"), "Review sync control should turn synchronization off.");
        surfaces[0].Zoom(2);
        Check(Math.Abs(ZoomRatio(surfaces[0]) - 2) < 0.02 && Math.Abs(ZoomRatio(surfaces[1]) - 1) < 0.02, "With sync off, each review image should retain its independent zoom state.");
    }

    private static double ZoomRatio(ImageSurface surface)
    {
        var doc = surface.Document!;
        var fit = Math.Min(surface.View.ViewportWidth / doc.Width, surface.View.ViewportHeight / doc.Height);
        return surface.View.Scale / fit;
    }

    private static Button? FindButton(DependencyObject root, string label)
    {
        if (root is FrameworkElement element && element is Window { Content: DependencyObject content })
            return FindButton(content, label);
        if (root is Button button && string.Equals(button.Content?.ToString(), label, StringComparison.OrdinalIgnoreCase)) return button;
        if (root is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var found = FindButton(VisualTreeHelper.GetChild(root, i), label); if (found is not null) return found; }
        }
        else foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { var found = FindButton(child, label); if (found is not null) return found; }
        return null;
    }

    private static void SaveReviewPng(MainWindow window, FrameworkElement root, string fileName)
    {
        window.Content = null;
        using var host = new HwndSource(new HwndSourceParameters("YingView.App.Tests.HiddenRenderHost") { Width = 1280, Height = 800, WindowStyle = 0 });
        host.RootVisual = root;
        var width = Math.Max(1, (int)Math.Ceiling(window.Width)); var height = Math.Max(1, (int)Math.Ceiling(window.Height));
        RenderToPng(root, width, height, fileName);
        host.RootVisual = null;
        window.Content = root;
    }

    private static void SaveWindowContentReviewPng(Window window, string fileName, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        using var host = new HwndSource(new HwndSourceParameters("YingView.App.Tests.HiddenContentRenderHost") { Width = width, Height = height, WindowStyle = 0 });
        host.RootVisual = content;
        RenderToPng(content, width, height, fileName);
        host.RootVisual = null;
        window.Content = content;
    }

    private static void RenderToPng(FrameworkElement root, int width, int height, string fileName)
    {
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var path = Path.Combine(Output, fileName);
        using var stream = File.Create(path); new PngBitmapEncoder { Frames = { BitmapFrame.Create(bitmap) } }.Save(stream);
        Console.WriteLine("Offscreen review PNG: " + path);
    }

    private static Task InvokeAsync(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(target, args)!;
    }

    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void InvokeVoid(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static object Property(object target, string name) => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Pump(Task task)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }
    private static void SpinUntil(Func<bool> done, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!done())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("Timed out waiting for the detached app operation.");
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Background, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
            timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        }
    }
    private static (long Items, long Bytes) QueryRecycleBin(string root)
    {
        var info = new Native.QueryRecycleBinInfo { Size = (uint)Marshal.SizeOf<Native.QueryRecycleBinInfo>() };
        Marshal.ThrowExceptionForHR(Native.SHQueryRecycleBin(root, ref info)); return (info.Items, info.Bytes);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct QueryRecycleBinInfo { public uint Size; public long Bytes; public long Items; }
        [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode, ExactSpelling = true)] internal static extern int SHQueryRecycleBin(string rootPath, ref QueryRecycleBinInfo info);
    }
}
