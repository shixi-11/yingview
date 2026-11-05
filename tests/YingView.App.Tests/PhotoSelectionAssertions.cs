using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using YingView;
using YingView.Core;
using YingView.Files;
using YingView.Review;

namespace YingView.AppTests;

internal static class PhotoSelectionAssertions
{
    internal static async Task Run(string output)
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var folder = Path.Combine(output, "photo-selection"); Directory.CreateDirectory(folder);
        var settings = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
        var first = Path.Combine(folder, "01.png"); var second = Path.Combine(folder, "02.png"); var third = Path.Combine(folder, "03.png");
        foreach (var path in new[] { first, second, third })
        {
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            using var stream = File.Create(path);
            new System.Windows.Media.Imaging.PngBitmapEncoder { Frames = { System.Windows.Media.Imaging.BitmapFrame.Create(bitmap) } }.Save(stream);
        }
        var store = new PhotoSelectionStore(settings);
        store.Set("favorites", [first, third], true);
        Check(new PhotoSelectionStore(settings).Read("favorites").SetEquals([first, third]), "Favorites must survive a store reload.");
        var secondStore = new PhotoSelectionStore(settings);
        await Task.WhenAll(Task.Run(() => store.Set("client:A", [first], true)), Task.Run(() => secondStore.Set("client:B", [second], true)));
        Check(store.Read("client:A").SetEquals([first]) && store.Read("client:B").SetEquals([second]) && store.Read("favorites").Count == 2, "Concurrent writes must preserve different client lists and photographer favorites.");
        var corruptPath = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
        foreach (var corrupt in new[] { "{", "{}", "{\"Version\":99,\"Lists\":{}}" })
        {
            File.WriteAllText(corruptPath, corrupt); bool rejected = false;
            try { new PhotoSelectionStore(corruptPath).Set("favorites", [first], true); } catch { rejected = true; }
            Check(rejected && File.ReadAllText(corruptPath) == corrupt, "Corrupt/future lists must not be silently overwritten.");
        }
        var viewPreferences = new Preferences(Path.Combine(folder, "view-options.json"));
        int recycleCalls = 0;
        var window = new PhotoSelectionWindow([first, second, third], first, false, store, (paths, _) =>
        {
            recycleCalls++; var snapshot = paths.ToArray();
            return Task.FromResult(new RecycleBinService.BatchResult([snapshot[0]], new Dictionary<string, string> { [snapshot[1]] = "Fixture failure" }, false));
        }, viewPreferences);
        object Field(string name) => typeof(PhotoSelectionWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        void Invoke(string name, params object[] args) => typeof(PhotoSelectionWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        using var inputSource = new HwndSource(new HwndSourceParameters("YingView.PhotoSelection.HiddenImeSource") { Width = 1, Height = 1, WindowStyle = 0 });
        System.Windows.Input.KeyEventArgs ImeKey(Window target, System.Windows.Input.Key key)
        {
            var args = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, inputSource, Environment.TickCount, key) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            typeof(System.Windows.Input.KeyEventArgs).GetMethod("MarkImeProcessed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(args, null);
            Check(args.Key == System.Windows.Input.Key.ImeProcessed, "Fixture must simulate an IME-preprocessed key.");
            target.RaiseEvent(args); return args;
        }
        var preview = new PhotoPreviewWindow(first, false);
        Check(ImeKey(preview, System.Windows.Input.Key.F).Handled, "Preview must handle Fit while Chinese IME is active."); preview.Close();
        var compare = new ReviewWindow([first, second], first, false);
        Check(ImeKey(compare, System.Windows.Input.Key.F).Handled, "Comparison must handle Fit while Chinese IME is active."); compare.Close();
        var selection = (PhotoSelection)Field("_selection");
        selection.Replace([second]); Invoke("ApplySelection");
        Check(ImeKey(window, System.Windows.Input.Key.P).Handled && store.Read("favorites").Contains(second), "IME-preprocessed P must favorite the selection.");
        Check(ImeKey(window, System.Windows.Input.Key.U).Handled && !store.Read("favorites").Contains(second), "IME-preprocessed U must unfavorite the selection.");
        selection.Replace([second]); Invoke("ApplySelection");
        Check(((Button)Field("_compare")).IsEnabled && window.BuildComparison() is { Files.Length: 3, Current: var start } && start == second, "One selected photo must open comparison from that photo with another available.");
        selection.Replace([first, third]); Invoke("ApplySelection");
        Check(window.BuildComparison()!.Value.Files.SequenceEqual([first, third]), "Two selected photos must compare exactly that pair.");
        Check(((Button)Field("_compare")).IsEnabled, "Two non-adjacent selected photos must enable comparison.");
        await window.DeleteSelectedAsync(_ => false);
        Check(recycleCalls == 0 && window.RecycledFiles.Count == 0, "Cancelling batch confirmation must perform no file operation.");
        selection.Replace([first, third]); Invoke("ApplySelection");
        await window.DeleteSelectedAsync(_ => true);
        Check(recycleCalls == 1 && window.RecycledFiles.SequenceEqual([first]) && selection.Selected.Contains(third), "Partial batch failure must remove only successful files and retain failed selections.");
        ((CheckBox)Field("_onlyFavorites")).IsChecked = true;
        Check(selection.Selected.Contains(third), "Favorite filtering must retain selected favorites.");
        Invoke("SaveFavorites", new[] { third }, false);
        Check(selection.Selected.Count == 0, "Unfavorited hidden photos must not remain selected for destructive actions.");
        Check(window.BuildComparison() is null, "Comparing a filtered list must not silently include hidden images.");
        var root = (FrameworkElement)window.Content;
        foreach (var width in new[] { 720, 1120 })
        {
            root.Measure(new Size(width, 700)); root.Arrange(new Rect(0, 0, width, 700)); root.UpdateLayout();
            Check(root.DesiredSize.Width <= width + 1, "Photo selection controls must fit the minimum viewer width.");
        }
        window.Close();
        var viewer = new MainWindow(new Preferences(Path.Combine(folder, "viewer.json")) { Language = "en" }, []);
        typeof(MainWindow).GetField("_files", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(viewer, new[] { first, second });
        await viewer.ApplyRecycledSelectionAsync([first], first, 0);
        var remaining = (string[])typeof(MainWindow).GetField("_files", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewer)!;
        Check(remaining.SequenceEqual([second]), "Batch removal must not expand an explicitly opened subset to unrelated folder images.");
        await viewer.ApplyRecycledSelectionAsync([second], second, 0);
        remaining = (string[])typeof(MainWindow).GetField("_files", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewer)!;
        Check(remaining.Length == 0, "Removing the last selected image must show an empty viewer without scanning its folder.");
        await viewer.ApplyRecycledSelectionAsync([second], second, 0, [second]);
        remaining = (string[])typeof(MainWindow).GetField("_files", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewer)!;
        Check(remaining.SequenceEqual([second]), "A photo reimported at a previously moved path must remain in the final scope.");
        viewer.Close();
        var importer = new PhotoSelectionWindow([], "", false, store);
        Check(importer.CurrentFiles.Count == 0, "Selection can start before opening a photo.");
        await importer.ImportPathsAsync([folder]);
        Check(importer.ScopeChanged && importer.CurrentFiles.SequenceEqual([first, second, third]), "Opening a folder must load all supported images in natural order.");
        await importer.ImportPathsAsync([third, first]);
        Check(importer.CurrentFiles.SequenceEqual([first, third]), "Dropped explicit files must preserve their scope.");
        var favoritesBeforeClear = store.Read("favorites");
        importer.ClearImported();
        Check(importer.ScopeChanged && importer.CurrentFiles.Count == 0 && File.Exists(first) && File.Exists(third) && store.Read("favorites").SetEquals(favoritesBeforeClear), "Clear imported must clear only the session scope while preserving files and favorites.");
        importer.Close();
        var manyFiles = Enumerable.Range(0, 61).Select(i => Path.Combine(folder, $"page{i:000}.png")).ToArray();
        var paged = new PhotoSelectionWindow(manyFiles, manyFiles[50], false, store, preferences: viewPreferences);
        object PagedField(string name) => typeof(PhotoSelectionWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(paged)!;
        ((ComboBox)PagedField("_pageSizeChoice")).SelectedItem = 24;
        paged.FocusPhoto(manyFiles[50]);
        Check((int)PagedField("_page") == 2 && ((PhotoSelection)PagedField("_selection")).Selected.SetEquals([manyFiles[50]]), "Returning from preview must select the current photo and reveal its page.");
        ((Slider)PagedField("_thumbnailSize")).Value = 260;
        typeof(PhotoSelectionWindow).GetMethod("SaveThumbnailSize", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(paged, null);
        var reloadedView = Preferences.Load(Path.Combine(folder, "view-options.json"));
        Check(reloadedView.PhotoPageSize == 24 && reloadedView.PhotoThumbnailSize == 260 && (int)PagedField("_page") == 2, "Page and thumbnail choices must persist without losing the current position.");
        ((ComboBox)PagedField("_pageSizeChoice")).SelectedItem = 2;
        Check(((System.Collections.ICollection)PagedField("_rows")).Count == 2 && (int)PagedField("_page") == 25, "A two-photo page must retain the current photo and paginate it correctly.");
        paged.Close();
        var navigable = new PhotoPreviewWindow([first, second, third], first, false);
        await navigable.LoadCurrentAsync(); await navigable.NavigateAsync(1);
        Check(navigable.CurrentPath == second && navigable.CurrentDocument is not null, "Preview Next must change and load the photo.");
        await navigable.NavigateAsync(99); Check(navigable.CurrentPath == third, "Preview must stop at the last photo.");
        await navigable.NavigateAsync(-99); Check(navigable.CurrentPath == first, "Preview must stop at the first photo."); navigable.Close();
        var slow = new TaskCompletionSource<YingView.Imaging.ImageDocument>();
        var fast = new TaskCompletionSource<YingView.Imaging.ImageDocument>();
        var racing = new PhotoPreviewWindow([first, second], first, false, path => path == first ? slow.Task : fast.Task);
        var oldLoad = racing.LoadCurrentAsync(); var newLoad = racing.NavigateAsync(1);
        var expectedDocument = YingView.Imaging.ImageLoader.Load(second);
        fast.SetResult(expectedDocument); await newLoad;
        slow.SetResult(YingView.Imaging.ImageLoader.Load(first)); await oldLoad;
        Check(racing.CurrentPath == second && ReferenceEquals(racing.CurrentDocument, expectedDocument), "Late preview loads must not overwrite the newly navigated photo."); racing.Close();
        var moveRoot = Path.Combine(folder, Guid.NewGuid().ToString("N"));
        var sourceFolder = Path.Combine(moveRoot, "source"); var destination = Path.Combine(moveRoot, "destination");
        Directory.CreateDirectory(sourceFolder); Directory.CreateDirectory(destination);
        var moving = Path.Combine(sourceFolder, "move.png"); var conflicted = Path.Combine(sourceFolder, "same.png");
        var moved = Path.Combine(destination, "move.png"); var existing = Path.Combine(destination, "same.png");
        File.Copy(first, moving); File.Copy(second, conflicted); byte[] sentinel = [7, 19, 23]; File.WriteAllBytes(existing, sentinel);
        store.Set("favorites", [moving], true);
        var mover = new PhotoSelectionWindow([moving, conflicted], moving, false, store, preferences: viewPreferences);
        var moveSelection = (PhotoSelection)typeof(PhotoSelectionWindow).GetField("_selection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mover)!;
        moveSelection.Replace([moving, conflicted]); await mover.MoveSelectedAsync(destination);
        Check(!File.Exists(moving) && File.Exists(moved) && mover.MovedFiles.Contains(moving), "Confirmed folder move must relocate the selected file.");
        Check(File.Exists(conflicted) && File.ReadAllBytes(existing).SequenceEqual(sentinel) && moveSelection.Selected.Contains(conflicted), "Same-name destinations must remain unchanged and conflicted photos must stay selected.");
        Check(store.Read("favorites").Contains(moved) && !store.Read("favorites").Contains(moving), "Favorites must follow a successful folder move.");
        mover.RemoveSelectedFromScope();
        Check(mover.CurrentFiles.Count == 0 && File.Exists(conflicted) && mover.MovedFiles.Contains(moving), "Remove from selection must leave the file intact and preserve earlier move results."); mover.Close();
        // Validate every target before invoking even the first Shell operation.
        var disposable = Path.Combine(folder, "validation-only.txt"); File.WriteAllText(disposable, "fixture");
        bool batchRejected = false;
        try { await RecycleBinService.RecycleManyAsync([disposable, folder], IntPtr.Zero); } catch (ArgumentException) { batchRejected = true; }
        Check(batchRejected && File.Exists(disposable), "Invalid later batch targets must stop deletion before the first file.");
        Console.WriteLine("Photo selection persistence, folder import safeguards and batch outcome assertions passed.");
    }
}
