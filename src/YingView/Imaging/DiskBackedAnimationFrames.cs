using System.Collections;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YingView.Imaging;

// Coalesced full-resolution pixels stay on disk; playback keeps at most two decoded frames.
internal sealed class DiskBackedAnimationFrames : IReadOnlyList<BitmapSource>, IDisposable
{
    private readonly FileStream _stream;
    private readonly int _width, _height, _frameBytes;
    private readonly object _gate = new();
    private readonly Dictionary<int, BitmapSource> _cache = new();
    private readonly Queue<int> _order = new();
    private bool _disposed;
    internal string BackingPath { get; }
    public int Count { get; private set; }
    internal uint Width => (uint)_width;
    internal uint Height => (uint)_height;
    internal long EstimatedBytes => (long)_frameBytes * 3;

    internal DiskBackedAnimationFrames(int width, int height)
    {
        _width = width; _height = height; _frameBytes = checked(width * height * 4);
        var folder = Path.Combine(Path.GetTempPath(), "YingView-frames");
        Directory.CreateDirectory(folder);
        BackingPath = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".frames");
        _stream = new FileStream(BackingPath, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.Read | FileShare.Delete, 4096, FileOptions.RandomAccess | FileOptions.DeleteOnClose);
    }
    internal void Append(byte[] pixels)
    {
        if (pixels.Length != _frameBytes) throw new InvalidDataException("Animation frame dimensions are inconsistent.");
        _stream.Write(pixels); Count++;
    }
    internal void Complete() => _stream.Flush();
    public BitmapSource this[int index]
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                if (_cache.TryGetValue(index, out var cached)) return cached;
                var pixels = new byte[_frameBytes];
                _stream.Position = (long)index * _frameBytes;
                _stream.ReadExactly(pixels);
                var bitmap = BitmapSource.Create(_width, _height, 96, 96, PixelFormats.Bgra32,
                    null, pixels, checked(_width * 4));
                bitmap.Freeze();
                if (_cache.Count == 2) _cache.Remove(_order.Dequeue());
                _cache.Add(index, bitmap); _order.Enqueue(index);
                return bitmap;
            }
        }
    }
    public IEnumerator<BitmapSource> GetEnumerator()
    {
        for (var i = 0; i < Count; i++) yield return this[i];
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _cache.Clear(); _order.Clear(); _stream.Dispose();
        }
    }
}
