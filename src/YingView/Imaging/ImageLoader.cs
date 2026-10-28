using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace YingView.Imaging;

public static class ImageLoader
{
    private const long AnimationBudget = 256L * 1024 * 1024;
    private const uint MaxFrames = 200;
    private const long MaxPixels = 100_000_000;
    private static readonly Dictionary<string, MagickFormat> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = MagickFormat.Png, [".jpg"] = MagickFormat.Jpeg, [".jpeg"] = MagickFormat.Jpeg,
        [".gif"] = MagickFormat.Gif, [".webp"] = MagickFormat.WebP, [".avif"] = MagickFormat.Avif,
        [".bmp"] = MagickFormat.Bmp, [".tif"] = MagickFormat.Tiff, [".tiff"] = MagickFormat.Tiff,
        [".ico"] = MagickFormat.Ico, [".heic"] = MagickFormat.Heic, [".heif"] = MagickFormat.Heic
    };
    public static IReadOnlyList<string> Extensions { get; } = Formats.Keys.ToArray();
    private static class MagickRuntime
    {
        static MagickRuntime()
        {
            ResourceLimits.Memory = 512UL * 1024 * 1024;
            ResourceLimits.Disk = 1024UL * 1024 * 1024;
            ResourceLimits.ListLength = MaxFrames + 1;
            ResourceLimits.Width = 100_000;
            ResourceLimits.Height = 100_000;
            ResourceLimits.Thread = 2;
            ResourceLimits.MaxProfileSize = 16UL * 1024 * 1024;
        }
        internal static void Initialize() { }
    }
    public static bool Supports(string path) => !string.IsNullOrWhiteSpace(path) && Formats.ContainsKey(Path.GetExtension(path));
    private static MagickReadSettings Settings(string path, uint frames) => new() { Format = Formats[Path.GetExtension(path)], FrameIndex = 0, FrameCount = frames };
    private static void ValidateSize(IMagickImage image)
    {
        if ((long)image.Width * image.Height > MaxPixels) throw new InvalidDataException("Image exceeds the 100-million-pixel limit.");
    }
    private static void Prepare(IMagickImage<byte> image)
    {
        image.AutoOrient();
        if (image.GetColorProfile() is not null) image.TransformColorSpace(ColorProfiles.SRGB);
        else if (image.ColorSpace == ColorSpace.CMYK) image.ColorSpace = ColorSpace.sRGB;
    }
    private static BitmapSource Bitmap(IMagickImage<byte> image)
    {
        int w = checked((int)image.Width), h = checked((int)image.Height);
        image.Depth = 8;
        var pixels = image.ToByteArray(MagickFormat.Bgra);
        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, checked(w * 4));
        bitmap.Freeze(); return bitmap;
    }
    public static ImageDocument Load(string path) => Load(path, AnimationBudget);
    internal static ImageDocument Load(string path, long animationBudget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Supports(path)) throw new InvalidDataException("Unsupported image extension.");
        if (NativeImageLoader.TryLoad(path) is { } native) return native;
        MagickRuntime.Initialize();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var info = new MagickImage();
        info.Ping(stream, Settings(path, 1)); ValidateSize(info);
        stream.Position = 0;
        bool mayAnimate = Path.GetExtension(path).ToLowerInvariant() is ".gif" or ".webp" or ".png";
        using var images = new MagickImageCollection();
        images.Read(stream, Settings(path, mayAnimate ? MaxFrames + 1 : 1));
        if (images.Count == 0) throw new InvalidDataException("The file contains no image.");
        if (images.Count > MaxFrames) throw new InvalidDataException("Animation exceeds the 200-frame limit.");
        string format = images[0].Format.ToString().ToUpperInvariant();
        long canvasPixels = Math.Max((long)info.Width * info.Height, (long)images[0].Page.Width * images[0].Page.Height);
        long decodedBytes = checked(canvasPixels * 4 * images.Count);
        if (images.Count > 1 && decodedBytes > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Animation exceeds the temporary-frame storage limit.");
        if (images.Count > 1) images.Coalesce();
        DiskBackedAnimationFrames? storage = null;
        try
        {
            bool useDisk = images.Count > 1 && decodedBytes > animationBudget;
            var frames = new List<BitmapSource>(); var delays = new List<int>();
            foreach (var frame in images)
            {
                ValidateSize(frame); Prepare(frame);
                if (!useDisk) frames.Add(Bitmap(frame));
                else
                {
                    storage ??= new DiskBackedAnimationFrames((int)frame.Width, (int)frame.Height);
                    if (frame.Width != storage.Width || frame.Height != storage.Height)
                        throw new InvalidDataException("Animation frame dimensions are inconsistent.");
                    frame.Depth = 8;
                    storage.Append(frame.ToByteArray(MagickFormat.Bgra));
                }
                delays.Add((int)Math.Clamp(frame.AnimationDelay * 1000L / Math.Max(1, frame.AnimationTicksPerSecond), 20, 60_000));
            }
            storage?.Complete();
            IReadOnlyList<BitmapSource> result = storage is null ? frames.AsReadOnly() : storage;
            int width = result[0].PixelWidth, height = result[0].PixelHeight;
            return new ImageDocument(result, delays.AsReadOnly(), width, height, format,
                storage?.EstimatedBytes ?? frames.Sum(f => (long)f.PixelWidth * f.PixelHeight * 4),
                null, Path.GetFullPath(path), (int)images[0].AnimationIterations);
        }
        catch { storage?.Dispose(); throw; }
    }

    public static BitmapSource LoadThumbnail(string path, int maxPixels = 160)
    {
        if (maxPixels < 1) throw new ArgumentOutOfRangeException(nameof(maxPixels));
        if (!Supports(path)) throw new InvalidDataException("Unsupported image extension.");
        MagickRuntime.Initialize();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var image = new MagickImage();
        image.Ping(stream, Settings(path, 1)); ValidateSize(image); stream.Position = 0;
        var settings = Settings(path, 1);
        settings.SetDefine(MagickFormat.Jpeg, "size", $"{maxPixels * 2}x{maxPixels * 2}");
        image.Read(stream, settings); Prepare(image);
        if (image.Width > maxPixels || image.Height > maxPixels) image.Thumbnail((uint)maxPixels, (uint)maxPixels);
        return Bitmap(image);
    }
}
