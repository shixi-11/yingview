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
        int recycleCalls = 0;
        var window = new PhotoSelectionWindow([first, second, third], first, false, store, (paths, _) =>
        {
            recycleCalls++; var snapshot = paths.ToArray();
            return Task.FromResult(new RecycleBinService.BatchResult([snapshot[0]], new Dictionary<string, string> { [snapshot[1]] = "Fixture failure" }, false));
        });
        object Field(string name) => typeof(PhotoSelectionWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        void Invoke(string name, params object[] args) => typeof(PhotoSelectionWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        var selection = (PhotoSelection)Field("_selection");
        selection.Replace([first, third]); Invoke("ApplySelection");
        Check(((Button)Field("_compare")).IsEnabled, "Two non-adjacent selected photos must enable comparison.");
        await window.DeleteSelectedAsync(_ => false);
        Check(recycleCalls == 0 && window.RecycledFiles.Count == 0, "Cancelling batch confirmation must perform no file operation.");
        ((ComboBox)Field("_role")).SelectedIndex = 1;
        selection.Replace([first, third]); Invoke("ApplySelection");
        await window.DeleteSelectedAsync(_ => true);
        Check(recycleCalls == 0 && !((Button)Field("_trash")).IsEnabled, "Client mode must disable and guard deletion.");
        ((TextBox)Field("_clientName")).Text = "Customer A";
        Invoke("SaveFavorites", new[] { second }, true);
        Check(store.Read("client:Customer A").Contains(second) && store.Read("favorites").SetEquals([first, third]), "Client picks must not overwrite photographer favorites.");
        ((ComboBox)Field("_role")).SelectedIndex = 0;
        selection.Replace([first, third]); Invoke("ApplySelection");
        await window.DeleteSelectedAsync(_ => true);
        Check(recycleCalls == 1 && window.RecycledFiles.SequenceEqual([first]) && selection.Selected.Contains(third), "Partial batch failure must remove only successful files and retain failed selections.");
        ((CheckBox)Field("_onlyFavorites")).IsChecked = true;
        Check(selection.Selected.Contains(third), "Favorite filtering must retain selected favorites.");
        Invoke("SaveFavorites", new[] { third }, false);
        Check(selection.Selected.Count == 0, "Unfavorited hidden photos must not remain selected for destructive actions.");
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
        viewer.Close();
        // Validate every target before invoking even the first Shell operation.
        var disposable = Path.Combine(folder, "validation-only.txt"); File.WriteAllText(disposable, "fixture");
        bool batchRejected = false;
        try { await RecycleBinService.RecycleManyAsync([disposable, folder], IntPtr.Zero); } catch (ArgumentException) { batchRejected = true; }
        Check(batchRejected && File.Exists(disposable), "Invalid later batch targets must stop deletion before the first file.");
        Console.WriteLine("Photo selection persistence, client safeguards and batch outcome assertions passed.");
    }
}
