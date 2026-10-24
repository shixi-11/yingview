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
using YingView.Files;

internal static class Program
{
    private static readonly string Output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "output", "app-tests"));

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--verify-latest-choice-format")
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var actual = DefaultAppService.GetAssociations("zh");
            var writer = LatestAssociationChoice.OpenVerified(identity.User!.Value, actual);
            if (writer is null) throw new Exception("No UserChoiceLatest records found.");
            var report = actual.Select(a => new { a.Extension, HashMatchesLatestChoice = writer.Matches(a.Extension) }).ToArray();
            File.WriteAllText(Path.GetFullPath(args[1]), System.Text.Json.JsonSerializer.Serialize(report,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Read-only latest hash matches: {report.Count(a => a.HashMatchesLatestChoice)}/{report.Length}.");
            return 0;
        }
        if (args.Length == 2 && args[0] == "--make-shell-fixtures")
        {
            var folder = Path.GetFullPath(args[1]); Directory.CreateDirectory(folder);
            var formats = new (string Extension, ImageMagick.MagickFormat Format)[]
            {
                ("png", ImageMagick.MagickFormat.Png), ("jpg", ImageMagick.MagickFormat.Jpeg),
                ("jpeg", ImageMagick.MagickFormat.Jpeg), ("gif", ImageMagick.MagickFormat.Gif),
                ("webp", ImageMagick.MagickFormat.WebP), ("avif", ImageMagick.MagickFormat.Avif),
                ("bmp", ImageMagick.MagickFormat.Bmp), ("tif", ImageMagick.MagickFormat.Tiff),
                ("tiff", ImageMagick.MagickFormat.Tiff), ("ico", ImageMagick.MagickFormat.Ico)
            };
            foreach (var (extension, format) in formats)
            {
                var path = Path.Combine(folder, "association-test." + extension);
                using (var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Black, 128, 96)) image.Write(path, format);
                var document = YingView.Imaging.ImageLoader.Load(path);
                Check(document.Width > 0 && document.Height > 0 && document.Frames.Count > 0, "Shell fixtures must decode successfully before use.");
            }
            Console.WriteLine("Created and decoded all 10 shell-opening fixtures.");
            return 0;
        }
        if (args.Length == 2 && args[0] == "--verify-default-choice-format")
        {
            var actual = DefaultAppService.GetAssociations("zh");
            var writer = LegacyAssociationChoice.OpenVerified(actual);
            File.WriteAllText(Path.GetFullPath(args[1]), System.Text.Json.JsonSerializer.Serialize(
                actual.Select(a => new { a.Extension, a.ProgId, HashMatchesEffectiveChoice = writer.MatchesEffective(a) }),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("The effective choice format was verified without modifying associations.");
            return 0;
        }
        if (args.Length == 3 && args[0] == "--report-defaults")
        {
            File.WriteAllText(Path.GetFullPath(args[2]), System.Text.Json.JsonSerializer.Serialize(
                new { Windows = Environment.OSVersion.VersionString, Associations = DefaultAppService.GetAssociations(args[1]) },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        // Explicit local opt-in only: this mode changes the signed-in user's real image defaults.
        if (args.Length == 3 && args[0] == "--set-defaults-for-installed-client")
        {
            var plan = DefaultAppService.CreateRegistrationPlan("zh", Path.GetFullPath(args[1]));
            var before = DefaultAppService.GetAssociations("zh");
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var testApp = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            testApp.InitializeComponent(); testApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var calls = 0;
            DefaultAppService.BatchResult? result = null;
            var confirmation = new DefaultAppsWindow("zh", true, _ => throw new Exception("Bulk setup must not launch Settings."),
                DefaultAppService.GetAssociations, _ =>
                {
                    Interlocked.Increment(ref calls);
                    result = DefaultAppService.SetAllDefaults(plan);
                    return result;
                }, batch: true);
            confirmation.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Check(calls == 0, "Opening the real confirmation must not modify defaults.");
            var needsSetup = before.Any(a => !a.IsYingView);
            ((Button)Field(confirmation, "_openButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while ((bool)Field(confirmation, "_opening") && DateTime.UtcNow < deadline)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(20);
            }
            Check(calls == (needsSetup ? 1 : 0), "A single confirmation must invoke exactly one real batch operation.");
            result ??= new DefaultAppService.BatchResult(DefaultAppService.GetAssociations("zh"), "", new Dictionary<string, string>());
            SaveWindowContentReviewPng(confirmation, "default-apps-live-result-zh.png", 520, 330);
            var statusText = ((TextBlock)Field(confirmation, "_status")).Text;
            confirmation.Close();
            File.WriteAllText(Path.GetFullPath(args[2]), System.Text.Json.JsonSerializer.Serialize(new
                { Windows = Environment.OSVersion.VersionString, Before = before, ConfirmationClicks = 1, BatchInvocations = calls, StatusText = statusText, Result = result },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Verified {result.Associations.Count(a => a.IsYingView)}/{result.Associations.Count} image defaults. Backup: {result.BackupPath}");
            return result.AllSet ? 0 : 1;
        }
        VerifyDefaultAppPlan();
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
        var identitySetter = typeof(App).GetMethod("SetCurrentProcessExplicitAppUserModelID", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check((int)identitySetter.Invoke(null, new object[] { "Shixi.YingView.Tests" })! == 0,
            "The documented process identity API must accept a stable desktop identity before any WPF window is created.");
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
            VerifyFreeClientTerms(window, settings);
            VerifyLayout(window);
            YingView.AppTests.BackgroundAssertions.Run(window, Output);
            var originalBackground = settings.Background;
            window.Width = 1280; window.Height = 800;
            InvokeVoid(window, "SetCanvasBackground", "black");
            SaveReviewPng(window, (FrameworkElement)window.Content, "canvas-ui-black.png");
            InvokeVoid(window, "SetCanvasBackground", "white");
            SaveReviewPng(window, (FrameworkElement)window.Content, "canvas-ui-white.png");
            InvokeVoid(window, "ToggleCanvasBackground");
            Check(settings.Background == "black", "The background button must toggle white to black.");
            InvokeVoid(window, "ToggleCanvasBackground");
            Check(settings.Background == "white", "The background button must toggle black to white.");
            InvokeVoid(window, "SetCanvasBackground", originalBackground);
            var fixtures = CreateFixtures();
            VerifyStartupContent(fixtures[0]);
            VerifyFirstImageBeforeFolderScan(fixtures);
            try
            {
                Pump(InvokeAsync(window, "OpenPaths", (object)new[] { fixtures[0], fixtures[1], fixtures[2] }));
                VerifyRecovery(window, fixtures);
                VerifyReviewWindow(fixtures);
                Pump(InvokeAsync(window, "OpenPaths", (object)new[] { fixtures[0], fixtures[1], fixtures[2] }));
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
                VerifyStyleApply();
                VerifyDefaultAppsWindow();
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

    private static void VerifyDefaultAppPlan()
    {
        const string latestSample = "copyright (c) microsoft. all rights reserved {3822b7ca-c2f4-4889-b8cc-4ce39a8fb81c}.pdd01dcb06ea49f32a0b4deb148-0249-44c4-a8d3-5409e822c599msedgepdfs-1-5-21-673349297-2269585490-1023937497-500";
        Check(YingView.Files.AssociationHash.LatestAssociationHash.Compute(latestSample) == "JOBZ2dl4dKM=",
            "Newer choice hashes must match the pinned upstream canonical vector.");
        var rejectedPeriodic = false;
        try { YingView.Files.AssociationHash.LatestAssociationHash.Compute(new string(Enumerable.Range(0, 511).Select(i => (char)('a' + i % 26)).ToArray())); }
        catch (InvalidOperationException) { rejectedPeriodic = true; }
        Check(rejectedPeriodic, "Adversarial periodic input must be bounded instead of hanging status reads.");
        var hashVectors = new (string Input, string Expected)[]
        {
            ("a", ""), ("ab", ""), ("abcdefgh", "NRA+C4IVU9o="), ("hello world", "Drw6/KuEjS8="),
            ("映窗.png", "u9d3y44SW9g="), ("a Unicode window 🪟 in an image viewer", "Eg8SHiXeJQ8="),
            (".pngs-1-5-21-100-200-300-1001yingview.zh-cn01dcb06ea49f0000user choice set via windows user experience {d18b6dd5-6124-4341-9318-804003bafa0b}", "rGYHjSHkg6E=")
        };
        foreach (var (input, expected) in hashVectors)
            Check(YingView.Files.AssociationHash.LegacyAssociationHash.Compute(input) == expected,
                "Association hashes must match independently generated PS-SFTA vectors, including Unicode and the terminating NUL.");
        Check(LegacyAssociationChoice.BuildCanonical(".PNG", "S-1-5-21-TEST", "YingView.zh-CN", 600000009)
            .StartsWith(".pngs-1-5-21-testyingview.zh-cn0000000023c34600"),
            "Choice input must use invariant lowercase and floor the registry timestamp to its UTC minute.");
        Check(ClientBranding.DesktopAppId("zh") == "Shixi.YingView.ZhCN" && ClientBranding.DesktopAppId("en") == "Shixi.YingView.EnUS", "Installer, shortcut and process identities must distinguish the language clients consistently.");
        var plan = DefaultAppService.CreateRegistrationPlan("zh", Path.GetFullPath("YingView.exe"));
        Check(plan.DisplayName == "映窗" && plan.ProgId == "YingView.zh-CN" && plan.Extensions.Contains(".png") && !plan.Extensions.Contains(".heic"), "Default app registration must follow localized branding and verified formats.");
        Check(DefaultAppService.BuildSettingsUri("映窗") == "ms-settings:defaultapps?registeredAppUser=%E6%98%A0%E7%AA%97", "The system settings deep link must encode the real localized registration name.");
        Check(DefaultAppService.BuildSettingsUri("RealPackage!YingView", "registeredAUMID").Contains("registeredAUMID="), "Packaged clients must target their real application identity.");
    }

    private static void VerifyDefaultAppsWindow()
    {
        var before = DefaultAppService.GetAssociations("zh");
        Check(before.Count == 10 && before.Any(a => a.ProgId is not null), "Read-only status must query effective Windows associations through the native API.");
        var dialog = new DefaultAppsWindow("zh");
        SaveWindowContentReviewPng(dialog, "default-apps-zh.png", 520, 330);
        dialog.Close();
        var after = DefaultAppService.GetAssociations("zh");
        Check(before.SequenceEqual(after), "Opening or refreshing status must not silently change user defaults.");
        File.WriteAllText(Path.Combine(Output, "default-associations.json"), System.Text.Json.JsonSerializer.Serialize(after));

        var count = 0; var opened = 0;
        IReadOnlyList<DefaultAppService.Association> Snapshot(string _) => before.Select((a, i) =>
            new DefaultAppService.Association(a.Extension, i < count ? "YingView.zh-CN" : a.ProgId, i < count)).ToArray();
        var automatic = new DefaultAppsWindow("zh", true, _ =>
        {
            opened++;
            return new DefaultAppService.OpenResult(true, true, "ms-settings:defaultapps");
        }, Snapshot);
        automatic.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Check(opened == 1 && ((TextBlock)Field(automatic, "_message")).Text.Contains("自动更新"), "Opening setup from the menu must launch the Windows flow immediately and explain automatic result detection.");
        automatic.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Check(opened == 1, "Repeated Loaded events must not reopen Windows Settings.");
        count = 4;
        InvokeVoid(automatic, "OnActivated", EventArgs.Empty);
        Check(((TextBlock)Field(automatic, "_status")).Text.Contains("4/10"), "Returning to the app must automatically read partial default changes.");
        count = 10;
        InvokeVoid(automatic, "OnActivated", EventArgs.Empty);
        Check(!((Button)Field(automatic, "_openButton")).IsEnabled && ((TextBlock)Field(automatic, "_status")).Text.Contains("已是默认"), "Actual successful associations must switch to the completed state.");
        InvokeVoid(automatic, "StartSetup");
        Check(opened == 1, "A completed setup must not launch the Windows flow again.");
        automatic.Close();

        count = 0;
        var failed = new DefaultAppsWindow("en", true, _ => new DefaultAppService.OpenResult(true, false, "ms-settings:defaultapps"), Snapshot);
        failed.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Check(((Button)Field(failed, "_openButton")).IsEnabled && ((TextBlock)Field(failed, "_message")).Text.Contains("could not open"), "A failed system launch must remain retryable and must not claim that defaults changed.");
        failed.Close();

        count = 0; var applied = 0;
        using var releaseBatch = new ManualResetEventSlim(false);
        var standalone = new DefaultAppsWindow("zh", true, _ => throw new Exception("Standalone setup must not launch Settings."), Snapshot,
            _ =>
            {
                Interlocked.Increment(ref applied);
                releaseBatch.Wait();
                count = 4;
                return new DefaultAppService.BatchResult(Snapshot("zh"), "test-backup.json", new Dictionary<string, string>());
            }, batch: true);
        standalone.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Check(applied == 0 && ((Button)Field(standalone, "_openButton")).Content.ToString()!.Contains("确认"),
            "Standalone setup must wait for the one explicit confirmation, even when opened from the menu.");
        SaveWindowContentReviewPng(standalone, "default-apps-confirm-zh.png", 520, 330);
        ((Button)Field(standalone, "_openButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        InvokeVoid(standalone, "StartSetup");
        Check(!((Button)Field(standalone, "_openButton")).IsEnabled, "Setup must disable duplicate confirmations while working.");
        releaseBatch.Set();
        var batchDeadline = DateTime.UtcNow.AddSeconds(5);
        while ((bool)Field(standalone, "_opening") && DateTime.UtcNow < batchDeadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }
        Check(applied == 1 && ((TextBlock)Field(standalone, "_status")).Text.Contains("4/10")
            && ((TextBlock)Field(standalone, "_message")).Text.Contains("部分"),
            "One confirmation must invoke a single bulk operation and must accurately report partial failure.");
        count = 10;
        InvokeVoid(standalone, "OnActivated", EventArgs.Empty);
        Check(!((Button)Field(standalone, "_openButton")).IsEnabled, "All effective defaults must finish standalone setup.");
        standalone.Close();

        count = 0;
        var english = new DefaultAppsWindow("en", false, _ => throw new Exception(), Snapshot,
            _ => throw new Exception(), batch: true);
        SaveWindowContentReviewPng(english, "default-apps-confirm-en.png", 520, 330);
        Check(((TextBlock)Field(english, "_message")).Text.Contains("Confirm to open all 10"), "The English download must offer the same explicit bulk confirmation.");
        english.Close();

        var unsupportedCalls = 0; var fallbackOpened = 0;
        var unsupported = new DefaultAppsWindow("en", false, _ =>
            {
                fallbackOpened++;
                return new DefaultAppService.OpenResult(true, true, "ms-settings:defaultapps");
            }, Snapshot, _ => { Interlocked.Increment(ref unsupportedCalls); throw new NotSupportedException(); }, batch: true);
        InvokeVoid(unsupported, "StartSetup");
        batchDeadline = DateTime.UtcNow.AddSeconds(5);
        while ((bool)Field(unsupported, "_opening") && DateTime.UtcNow < batchDeadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }
        Check(unsupportedCalls == 1 && fallbackOpened == 0
            && ((Button)Field(unsupported, "_openButton")).Content.ToString() == "Open Windows Settings",
            "An unverified Windows choice format must stop bulk writes and offer the system flow.");
        InvokeVoid(unsupported, "StartSetup");
        Check(unsupportedCalls == 1 && fallbackOpened == 1, "Fallback must not keep retrying an unsupported registry scheme.");
        unsupported.Close();
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
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            pixels[i] = (byte)(80 + 100d * x / width);
            pixels[i + 1] = (byte)(95 + 110d * y / height);
            pixels[i + 2] = (byte)(200 - 80d * x / width);
            var inset = Math.Min(width, height) / 12;
            pixels[i + 3] = (byte)(x < inset || y < inset || x >= width - inset || y >= height - inset ? 128 : 255);
        }
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        var path = Path.Combine(dir, name + "-" + Guid.NewGuid().ToString("N") + ".png");
        using var stream = File.Create(path);
        new PngBitmapEncoder { Frames = { BitmapFrame.Create(bitmap) } }.Save(stream);
        return path;
    }

    private static void VerifyStartupContent(string fixture)
    {
        var directory = Path.Combine(Output, "startup"); Directory.CreateDirectory(directory);
        var image = Path.Combine(directory, "image with spaces.png"); File.Copy(fixture, image, true);
        var broken = Path.Combine(directory, "broken.png"); File.WriteAllText(broken, "Not an image");
        var welcome = new MainWindow(new Preferences(Path.Combine(directory, "welcome-settings.json")) { Language = "en" }, ["--test"]);
        Check(((FrameworkElement)Field(welcome, "EmptyPanel")).Visibility == Visibility.Visible,
            "Starting without a file must retain the welcome page.");
        welcome.Close();

        var prepared = new MainWindow(new Preferences(Path.Combine(directory, "prepared-settings.json")) { Language = "en" }, [image]);
        var preparing = prepared.OpenStartupAsync();
        Check(ReferenceEquals(preparing, prepared.OpenStartupAsync()), "Startup preparation must not launch the same decode twice.");
        Pump(preparing);
        Check(((ImageSurface)Field(prepared, "Surface")).Document is not null && !prepared.IsLoaded,
            "Startup preparation must make the requested image available before the window is shown or Loaded fires.");
        SaveReviewPng(prepared, (FrameworkElement)prepared.Content, "startup-prepared-image.png");
        var preparedSurface = (ImageSurface)Field(prepared, "Surface");
        Check(Math.Abs(preparedSurface.View.ViewportWidth - preparedSurface.ActualWidth) < 0.01
            && Math.Abs(preparedSurface.View.ViewportHeight - preparedSurface.ActualHeight) < 0.01,
            "The preloaded image must already fit the real viewport on its first arranged frame.");
        using (var capture = File.OpenRead(Path.Combine(Output, "startup-prepared-image.png")))
        {
            var firstFrame = new PngBitmapDecoder(capture, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var centerPixel = new byte[4];
            firstFrame.CopyPixels(new Int32Rect(firstFrame.PixelWidth / 2, firstFrame.PixelHeight / 2, 1, 1), centerPixel, 4, 0);
            Check(centerPixel.Take(3).Any(channel => channel > 40), "The first rendered startup frame must contain the image, rather than a black canvas.");
        }
        prepared.Close();

        var startup = new MainWindow(new Preferences(Path.Combine(directory, "image-settings.json")) { Language = "en" }, [image]);
        var panel = (FrameworkElement)Field(startup, "EmptyPanel");
        var surface = (ImageSurface)Field(startup, "Surface");
        var gate = (SemaphoreSlim)Field(startup, "_decodeGate");
        Check(panel.Visibility == Visibility.Collapsed && (bool)Field(startup, "_loading"),
            "A file launch must suppress the welcome page before the first render or Loaded event.");
        SaveReviewPng(startup, (FrameworkElement)startup.Content, "startup-before-loaded.png");
        gate.Wait();
        try
        {
            var pendingStartup = startup.OpenStartupAsync();
            startup.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Check(ReferenceEquals(pendingStartup, startup.OpenStartupAsync()), "Loaded must reuse the in-flight startup decode.");
            SpinUntil(() => string.Equals((string?)Field(startup, "_current"), image, StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(10));
            Check((bool)Field(startup, "_loading") && surface.Document is null && panel.Visibility == Visibility.Collapsed,
                "A delayed first decode must keep the welcome page hidden while loading.");
            SaveReviewPng(startup, (FrameworkElement)startup.Content, "startup-opening.png");
        }
        finally { gate.Release(); }
        SpinUntil(() => !(bool)Field(startup, "_loading"), TimeSpan.FromSeconds(10));
        Check(surface.Document is not null && panel.Visibility == Visibility.Collapsed,
            "The Loaded handler must open the actual requested file, including spaces in its path.");
        SaveReviewPng(startup, (FrameworkElement)startup.Content, "startup-image.png");

        Pump(InvokeAsync(startup, "OpenPaths", (object)new[] { broken }));
        Check(surface.Document is null && panel.Visibility == Visibility.Visible && (string?)Field(startup, "_error") is not null,
            "A corrupt image must show its recovery state after loading fails.");
        var recovery = InvokeAsync(startup, "OpenPaths", (object)new[] { image });
        Check(panel.Visibility == Visibility.Collapsed, "Opening another file must immediately dismiss the previous error page.");
        Pump(recovery);
        Check(surface.Document is not null && panel.Visibility == Visibility.Collapsed, "A valid image must recover from a failed open.");
        startup.Close();

        var missing = new MainWindow(new Preferences(Path.Combine(directory, "missing-settings.json")) { Language = "zh" }, [Path.Combine(directory, "missing.png")]);
        Check(((FrameworkElement)Field(missing, "EmptyPanel")).Visibility == Visibility.Collapsed, "Missing startup paths must not flash the welcome page either.");
        missing.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        SpinUntil(() => !(bool)Field(missing, "_loading"), TimeSpan.FromSeconds(10));
        Check(((FrameworkElement)Field(missing, "EmptyPanel")).Visibility == Visibility.Visible && ((TextBlock)Field(missing, "EmptyTitle")).Text == "图片已不存在",
            "A missing startup image must end in the specific recoverable error state.");
        missing.Close();
    }

    private static void VerifyFirstImageBeforeFolderScan(string[] fixtures)
    {
        using var releaseScan = new ManualResetEventSlim();
        var viewer = new MainWindow(new Preferences(Path.Combine(Output, "settings", "first-image.json")) { Language = "en" },
            readFolder: _ => { releaseScan.Wait(); return [fixtures[0], fixtures[1]]; });
        var surface = (ImageSurface)Field(viewer, "Surface");
        var open = InvokeAsync(viewer, "OpenPaths", (object)new[] { fixtures[0] });
        try
        {
            SpinUntil(() => surface.Document is not null, TimeSpan.FromSeconds(10));
            Check(!open.IsCompleted && !(bool)Field(viewer, "_loading"), "The image must be displayed and usable while its folder scan is still blocked.");
            Check(((string[])Field(viewer, "_files")).Length == 1, "Folder indexing must not delay the requested image.");
            SaveReviewPng(viewer, (FrameworkElement)viewer.Content, "first-image-before-folder.png");
        }
        finally { releaseScan.Set(); }
        Pump(open);
        Check(((string[])Field(viewer, "_files")).Length == 2, "Background indexing must restore sibling navigation.");
        Pump(InvokeAsync(viewer, "Navigate", 1));
        Check(string.Equals((string?)Field(viewer, "_current"), fixtures[1], StringComparison.OrdinalIgnoreCase), "Folder navigation must preserve the index after displaying the first image.");
        viewer.Close();

        using var releaseStaleScan = new ManualResetEventSlim();
        var stale = new MainWindow(new Preferences(Path.Combine(Output, "settings", "stale-index.json")) { Language = "en" },
            readFolder: _ => { releaseStaleScan.Wait(); return [fixtures[0], fixtures[1]]; });
        var firstOpen = InvokeAsync(stale, "OpenPaths", (object)new[] { fixtures[0] });
        try
        {
            SpinUntil(() => ((ImageSurface)Field(stale, "Surface")).Document is not null, TimeSpan.FromSeconds(10));
            Pump(InvokeAsync(stale, "OpenPaths", (object)new[] { fixtures[1], fixtures[2] }));
        }
        finally { releaseStaleScan.Set(); }
        Pump(firstOpen);
        Check(((string[])Field(stale, "_files")).SequenceEqual(new[] { fixtures[1], fixtures[2] }), "A late folder scan must not replace a newer open request.");
        stale.Close();

        var inaccessible = new MainWindow(new Preferences(Path.Combine(Output, "settings", "inaccessible-index.json")) { Language = "en" },
            readFolder: _ => throw new UnauthorizedAccessException());
        Pump(InvokeAsync(inaccessible, "OpenPaths", (object)new[] { fixtures[0] }));
        Check(((ImageSurface)Field(inaccessible, "Surface")).Document is not null, "An unreadable directory listing must not hide an independently readable image.");
        inaccessible.Close();
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

    private static void VerifyRecovery(MainWindow window, string[] fixtures)
    {
        var root = (FrameworkElement)Field(window, "Root");
        var directory = Path.Combine(Output, "recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Pump(InvokeAsync(window, "OpenPaths", (object)new[] { directory }));
        var surface = (ImageSurface)Field(window, "Surface");
        Check(surface.Document is null && (string?)Field(window, "_current") is null, "An empty folder must clear the previous image and deletion target.");
        Check(((Button)Field(window, "DeleteButton")).IsEnabled == false, "An empty folder must disable deletion.");
        var broken = Path.Combine(directory, "a-broken.png");
        File.WriteAllText(broken, "not an image");
        Pump(InvokeAsync(window, "OpenPaths", (object)new[] { broken }));
        Check(surface.Document is null && ((FrameworkElement)Field(window, "EmptyPanel")).Visibility == Visibility.Visible,
            "A corrupt file must display a recoverable empty state rather than the previous image.");
        Pump(InvokeAsync(window, "OpenPaths", (object)new[] { Path.Combine(directory, "missing.png") }));
        Check(surface.Document is null && (string?)Field(window, "_current") is null,
            "Opening a missing file must not silently select a different image from its folder.");
        var valid = Path.Combine(directory, "b-valid.png"); File.Copy(fixtures[0], valid);
        Pump(InvokeAsync(window, "OpenPaths", (object)new[] { valid }));
        Check(surface.Document is not null, "Opening a valid file must recover after a decode error.");
        Check(((FrameworkElement)Field(window, "Toast")).Visibility == Visibility.Collapsed, "A successful open must clear the previous file's error toast.");
        var moved = Path.Combine(directory, "c-moved.png"); File.Move(valid, moved);
        Pump(InvokeAsync(window, "ReloadCurrent"));
        Check(string.Equals((string?)Field(window, "_current"), broken, StringComparison.OrdinalIgnoreCase), "Refresh after an external move must re-enumerate the folder.");
        Pump(InvokeAsync(window, "Navigate", 1));
        Check(surface.Document is not null && string.Equals((string?)Field(window, "_current"), moved, StringComparison.OrdinalIgnoreCase), "Navigation must recover from a failed file in the refreshed folder.");
        using var source = new HwndSource(new HwndSourceParameters("YingView.App.Tests.FocusedControl") { Width = 1, Height = 1, WindowStyle = 0 });
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Delete)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = (Button)Field(window, "OpenButton") };
        window.RaiseEvent(key);
        Check(!key.Handled && File.Exists(moved), "Delete from a focused toolbar control must not delete an image.");
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
        var studio = new StyleStudioWindow(settings, _ => { }, scale => scalePreviews.Add(scale), _ => { });
        var cancel = FindButton(studio, "Cancel");
        Check(cancel is not null, "Style studio should expose its cancel action.");
        var slider = (Slider)Field(studio, "_scale");
        slider.Value = 1.2;
        SaveWindowContentReviewPng(studio, "style-studio-detached.png", 680, 760);
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
        var pickers = (ComboBox[])Field(review, "_pickers");
        pickers[0].SelectedIndex = 2;
        SpinUntil(() => surfaces[0].Document?.Width == 4200, TimeSpan.FromSeconds(20));
        Check(pickers[0].Template.FindName("PART_Popup", pickers[0]) is System.Windows.Controls.Primitives.Popup,
            "The styled file picker must retain the native dropdown template part.");
        var decisions = (Button[][])Field(review, "_decisionButtons");
        decisions[0][0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((Button)Field(review, "_exportButton")).IsEnabled, "A loaded, marked image should enable CSV export.");
        pickers[1].SelectedIndex = 2;
        SpinUntil(() => surfaces[1].Document?.Width == 4200, TimeSpan.FromSeconds(20));
        Check(((TextBlock[])Field(review, "_decisions")).All(t => t.Text == "Marked to keep"), "The same file must show the same session mark in both panes.");
        review.Close();
        Check(surfaces.All(s => s.Document is null), "Closing comparison must release both images and animation timers.");

        var originalSize = Application.Current.Resources["ControlTextSize"];
        try
        {
            Application.Current.Resources["ControlTextSize"] = 16.9;
            var chinese = new ReviewWindow(fixtures, fixtures[0], isZh: true);
            Pump(InvokeAsync(chinese, "LoadPairAsync", 0, 1));
            SaveWindowContentReviewPng(chinese, "review-900-zh-large-text.png", 900, 600);
            var export = (Button)Field(chinese, "_exportButton");
            Check(export.FontSize > 15 && export.ActualWidth > 80, "Comparison actions must follow the larger interface text setting.");
            chinese.Close();
        }
        finally { Application.Current.Resources["ControlTextSize"] = originalSize; }
    }

    private static void VerifyStyleApply()
    {
        var path = Path.Combine(Output, "settings", "style-apply.json");
        var settings = new Preferences(path) { Language = "zh", AppearanceUiVersion = 2 };
        ViewerTheme? preview = null;
        var studio = new StyleStudioWindow(settings, theme => preview = theme, _ => { }, _ => { });
        Check(FindButton(studio, "我的风格")?.IsEnabled == true, "The personal style library must be available without an entitlement.");
        InvokeVoid(studio, "SetTheme", ViewerTheme.BuiltIn[2]);
        ((TextBox)Field(studio, "_name")).Text = "山间苔色";
        ((Slider)Field(studio, "_scale")).Value = 1.15;
        ((CheckBox)Field(studio, "_compact")).IsChecked = true;
        var library = studio.CreateLibraryWindow();
        var list = ((DockPanel)library.Content).Children.OfType<ListBox>().Single();
        FindButton(library, "保存当前")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(list.Items.Cast<string>().Contains("山间苔色"), "A free client must be able to save a personal style.");
        InvokeVoid(studio, "SetTheme", ViewerTheme.BuiltIn[0]);
        list.SelectedItem = "山间苔色";
        FindButton(library, "载入")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(((TextBox)Field(studio, "_name")).Text == "山间苔色", "A free client must be able to load a saved style.");
        library.Close();
        SaveWindowContentReviewPng(studio, "style-studio-zh.png", 704, 760);
        Check(preview?.Name == "山间苔色", "Renaming a theme must update its live preview.");
        InvokeVoid(studio, "Apply");
        var saved = System.Text.Json.JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path))!;
        Check(studio.Applied && saved.Theme.Name == "山间苔色" && saved.Theme.Canvas == ViewerTheme.BuiltIn[2].Canvas
            && Math.Abs(saved.TextScale - 1.15) < 0.001 && saved.CompactToolbar && saved.PersonalStyles.Any(s => s.Name == "山间苔色"),
            "Apply must persist custom name, colors, text size, and compact layout without a Pro entitlement.");
    }

    private static void VerifyFreeClientTerms(MainWindow window, Preferences settings)
    {
        var language = settings.Language;
        try
        {
            foreach (var code in new[] { "en", "zh" })
            {
                settings.Language = code;
                InvokeVoid(window, "BuildMenu");
                var labels = ((MenuItem)Field(window, "MoreMenu")).Items.OfType<MenuItem>().Select(i => i.Header?.ToString() ?? "").ToArray();
                Check(labels.Contains(code == "zh" ? "外观设置…" : "Appearance…") && !labels.Any(s => s.Contains("Pro") || s.Contains("专业版")),
                    "The current release must expose appearance settings without a paid-plan entry.");
                var terms = window.GetLicenseText();
                Check(terms.Contains(code == "zh" ? "免费个人及商业使用" : "Free personal and commercial use") && terms.Contains("PolyForm Noncommercial License 1.0.0")
                    && terms.Contains(code == "zh" ? "书面" : "separate written permission"), "Both client languages must embed the commercial-use grant and separate source terms.");
            }
        }
        finally { settings.Language = language; InvokeVoid(window, "BuildMenu"); }
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
