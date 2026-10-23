using System.Runtime.InteropServices;
using YingView.Files;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Expect<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception(message);
}

var tempFixtureRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "YingView.FileTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempFixtureRoot);
var rejectedDirectoryFile = Path.Combine(tempFixtureRoot, "rejected-directory-check-" + Guid.NewGuid().ToString("N") + ".txt");
File.WriteAllText(rejectedDirectoryFile, "Disposable test fixture.");
Expect<ArgumentException>(() => RecycleBinService.RecycleAsync(tempFixtureRoot, IntPtr.Zero).GetAwaiter().GetResult(), "Directories must be rejected.");
Check(Directory.Exists(tempFixtureRoot), "Rejected directory must remain intact.");
Check(File.Exists(rejectedDirectoryFile), "Rejected directory attempt must not alter its child file.");
Expect<NotSupportedException>(() => RecycleBinService.RecycleAsync("\\\\example.invalid\\share\\image.png", IntPtr.Zero).GetAwaiter().GetResult(), "Network paths must be rejected.");
Expect<FileNotFoundException>(() => RecycleBinService.RecycleAsync(Path.Combine(tempFixtureRoot, "missing.png"), IntPtr.Zero).GetAwaiter().GetResult(), "Missing paths must be rejected.");
Check(await RecycleBinService.RecycleAsync(rejectedDirectoryFile, IntPtr.Zero), "The disposable directory-check fixture should be recyclable afterward.");
Check(!File.Exists(rejectedDirectoryFile), "The recycled directory-check fixture should leave its source path.");

await VerifyRecycleAsync(tempFixtureRoot, "Profile temporary-volume fixture");
var project = new DirectoryInfo(AppContext.BaseDirectory);
while (project is not null && !File.Exists(Path.Combine(project.FullName, "src", "YingView", "YingView.csproj"))) project = project.Parent;
if (project is null) throw new DirectoryNotFoundException("Run FileTests from a checkout containing src/YingView.");
var projectOutput = Path.Combine(project.FullName, "output");
await VerifyRecycleAsync(projectOutput, "Project-output fixture");
Directory.Delete(tempFixtureRoot);
Console.WriteLine("Recycle Bin service assertions passed for profile and project fixtures. Both were left in the Recycle Bin; no system dialogs were used.");

static (long Items, long Bytes) QueryRecycleBin(string root)
{
    var info = new Native.QueryRecycleBinInfo { Size = (uint)Marshal.SizeOf<Native.QueryRecycleBinInfo>() };
    Marshal.ThrowExceptionForHR(Native.SHQueryRecycleBin(root, ref info));
    return (info.Items, info.Bytes);
}

static async Task VerifyRecycleAsync(string directory, string label)
{
    Directory.CreateDirectory(directory);
    var fixtureDirectory = Path.Combine(directory, "YingView.FileTests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(fixtureDirectory);
    var fixtureFile = Path.Combine(fixtureDirectory, "recycle-test-" + Guid.NewGuid().ToString("N") + ".txt");
    var contents = "Disposable YingView fixture " + Guid.NewGuid().ToString("N");
    await File.WriteAllTextAsync(fixtureFile, contents);
    var fullPath = Path.GetFullPath(fixtureFile);
    var drive = Path.GetPathRoot(fullPath)!;
    var before = QueryRecycleBin(drive);
    var moved = await RecycleBinService.RecycleAsync(fullPath, IntPtr.Zero);
    Check(moved, $"{label}: Shell operation should complete without an interactive prompt.");
    Check(!File.Exists(fullPath), $"{label}: recycled fixture should disappear from its source.");
    var after = QueryRecycleBin(drive);
    Check(after.Items >= before.Items + 1, $"{label}: Recycle Bin item count should increase.");
    Check(after.Bytes >= before.Bytes + contents.Length, $"{label}: Recycle Bin data size should increase.");
    Directory.Delete(fixtureDirectory);
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct QueryRecycleBinInfo
    {
        public uint Size;
        public long Bytes;
        public long Items;
    }

    [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SHQueryRecycleBin(string rootPath, ref QueryRecycleBinInfo info);
}
