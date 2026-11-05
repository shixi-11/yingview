using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YingView.Review;

public sealed class PhotoSelectionStore
{
    private readonly string _path;
    public PhotoSelectionStore(string? path = null) => _path = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YingView", "photo-selections.json"));
    public HashSet<string> Read(string list) => new(ReadData().Lists.GetValueOrDefault(list) ?? [], StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Set(string list, IEnumerable<string> files, bool favorite)
    {
        if (string.IsNullOrWhiteSpace(list) || list.Length > 100) throw new ArgumentException("Invalid selection list name.", nameof(list));
        var paths = files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_path.ToUpperInvariant())))[..24];
        using var gate = new Mutex(false, "Local\\YingView.PhotoSelections." + name);
        bool locked;
        try { locked = gate.WaitOne(TimeSpan.FromSeconds(2)); } catch (AbandonedMutexException) { locked = true; }
        if (!locked) throw new IOException("The selection list is busy. Try again.");
        try
        {
            var data = ReadData();
            var set = new HashSet<string>(data.Lists.GetValueOrDefault(list) ?? [], StringComparer.OrdinalIgnoreCase);
            foreach (var file in paths) { if (favorite) set.Add(file); else set.Remove(file); }
            data.Lists[list] = set.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(data)); File.Move(temporary, _path, true); return set; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { gate.ReleaseMutex(); }
    }
    private Data ReadData()
    {
        if (!File.Exists(_path)) return new();
        if (new FileInfo(_path).Length > 64 * 1024 * 1024) throw new InvalidDataException("The selection file is too large.");
        var text = File.ReadAllText(_path);
        using var document = JsonDocument.Parse(text);
        if (!document.RootElement.TryGetProperty("Version", out var version) || !version.TryGetInt32(out var number) || number != 1
            || !document.RootElement.TryGetProperty("Lists", out var lists) || lists.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The selection file has an unsupported format.");
        var data = JsonSerializer.Deserialize<Data>(text) ?? throw new InvalidDataException("The selection file is empty.");
        if (data.Version != 1 || data.Lists is null || data.Lists.Any(x => x.Value is null || x.Value.Any(p => string.IsNullOrWhiteSpace(p) || !Path.IsPathFullyQualified(p))))
            throw new InvalidDataException("The selection file has an unsupported format.");
        return data;
    }
    public sealed class Data
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, string[]> Lists { get; set; } = new(StringComparer.Ordinal);
    }
}
