using System.Buffers.Binary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using ImageMagick;
using YingView.Imaging;

internal static class Program
{
    private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "output", "fixtures");
    private static int _passed;
    private static int _skipped;
    private static int _failed;

    [STAThread]
    private static int Main()
    {
        Directory.CreateDirectory(FixtureDirectory);
        Run("PNG Unicode/alpha/file sharing", TestPngUnicodeAlphaAndFileSharing);
        Run("JPEG EXIF orientation", TestJpegExifOrientation);
        Run("GIF frames/delays/disposal", TestGifFramesDelaysAndDisposal);
        Run("thumbnail bounds/first frame", TestThumbnailUsesFirstFrameAndBoundsSize);
        Run("still format decoders", TestStillFormats);
        Run("corrupt file handling", TestCorruptFile);
        Run("100-million-pixel limit", TestPixelLimit);
        Run("201-frame limit", TestAnimationFrameLimit);
        Console.WriteLine($"PASS: {_passed}; FAIL: {_failed}; SKIP: {_skipped}");
        return _failed == 0 ? 0 : 1;
    }

    private static void TestPngUnicodeAlphaAndFileSharing()
    {
        var path = Path.Combine(FixtureDirectory, "中文路径-透明测试.png");
        using (var image = SvgImage("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='2'><rect x='0' y='0' width='2' height='2' fill='#ff0000' fill-opacity='0.5'/><rect x='2' y='0' width='3' height='2' fill='#0000ff'/></svg>", transparent: true))
        {
            image.Depth = 8;
            image.ColorType = ColorType.TrueColorAlpha;
            image.Write(path, MagickFormat.Png32);
        }
        var document = ImageLoader.Load(path);
        Equal(4, document.Width, "Unicode PNG width");
        Equal(2, document.Height, "Unicode PNG height");
        Equal(1, document.Frames.Count, "PNG frame count");
        var pixels = Pixels(document.Frames[0]);
        PixelNear(document.Frames[0], 1, 1, 255, 0, 0, 128, 3, "half-alpha red keeps channel order and unpremultiplied color");
        PixelNear(document.Frames[0], 3, 0, 0, 0, 255, 255, 0, "opaque blue keeps channel order");

        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Pass("loaded image releases source file handle");
    }

    private static void TestJpegExifOrientation()
    {
        var path = Path.Combine(FixtureDirectory, "orientation.jpg");
        using (var image = SvgImage("<svg xmlns='http://www.w3.org/2000/svg' width='4' height='2'><rect width='4' height='1' fill='#ff0000'/><rect y='1' width='4' height='1' fill='#0000ff'/></svg>"))
        {
            image.Quality = 100;
            image.Write(path, MagickFormat.Jpeg);
        }
        InsertExifOrientation(path, 6);
        using (var probe = new MagickImage(path)) Equal(Orientation.RightTop, probe.Orientation, "fixture includes EXIF orientation 6");

        var loaded = ImageLoader.Load(path);
        Equal(2, loaded.Width, "EXIF oriented JPEG width");
        Equal(4, loaded.Height, "EXIF oriented JPEG height");
        PixelNear(loaded.Frames[0], 0, 0, 0, 0, 255, 255, 45, "EXIF orientation applied before rendering");
        Pass("JPEG EXIF orientation");
    }

    private static void TestGifFramesDelaysAndDisposal()
    {
        var path = Path.Combine(FixtureDirectory, "three-frame.gif");
        WriteGif(path, 3);
        var document = ImageLoader.Load(path);
        Equal(3, document.Frames.Count, "GIF frame count");
        Equal(50, document.FrameDelaysMs[0], "GIF first frame delay");
        Equal(70, document.FrameDelaysMs[1], "GIF second frame delay");
        Equal(90, document.FrameDelaysMs[2], "GIF third frame delay");
        for (var i = 0; i < 3; i++)
        {
            Equal(8, document.Frames[i].PixelWidth, $"coalesced GIF frame {i} width");
            Equal(8, document.Frames[i].PixelHeight, $"coalesced GIF frame {i} height");
        }
        PixelNear(document.Frames[0], 2, 2, 255, 0, 0, 255, 10, "GIF first frame is red");
        PixelNear(document.Frames[1], 2, 2, 0, 255, 0, 255, 10, "GIF second frame overlays green on prior red");
        PixelNear(document.Frames[1], 6, 6, 0, 0, 0, 0, 0, "first frame Background disposal clears the previous canvas");
        PixelNear(document.Frames[2], 2, 2, 0, 0, 0, 0, 0, "Background disposal clears the prior full frame");
        PixelNear(document.Frames[2], 6, 6, 0, 0, 255, 255, 10, "GIF third frame shows blue after disposal");
        Pass("GIF disposal method is honored while coalescing");
    }

    private static void TestThumbnailUsesFirstFrameAndBoundsSize()
    {
        var gifPath = Path.Combine(FixtureDirectory, "three-frame.gif");
        using (var large = SvgImage("<svg xmlns='http://www.w3.org/2000/svg' width='800' height='400'><rect width='800' height='400' fill='#ff0000'/></svg>"))
            large.Write(Path.Combine(FixtureDirectory, "large-red.png"), MagickFormat.Png);

        var thumb = ImageLoader.LoadThumbnail(Path.Combine(FixtureDirectory, "large-red.png"), 160);
        Check(thumb.PixelWidth <= 160 && thumb.PixelHeight <= 160, "thumbnail dimensions fit within maxPixels");
        PixelNear(thumb, thumb.PixelWidth / 2, thumb.PixelHeight / 2, 255, 0, 0, 255, 0, "thumbnail pixel content");
        Pass("thumbnail max dimension");

        var animatedThumb = ImageLoader.LoadThumbnail(gifPath, 160);
        PixelNear(animatedThumb, animatedThumb.PixelWidth / 2, animatedThumb.PixelHeight / 2, 255, 0, 0, 255, 0, "animated thumbnail decodes only first frame");
        Pass("animated thumbnail uses first frame");
    }

    private static void TestStillFormats()
    {
        var cases = new (string Extension, MagickFormat Format, string Name, bool Optional)[]
        {
            ("webp", MagickFormat.WebP, "WebP", false),
            ("avif", MagickFormat.Avif, "AVIF", true),
            ("bmp", MagickFormat.Bmp, "BMP", false),
            ("tiff", MagickFormat.Tiff, "TIFF", false),
            ("ico", MagickFormat.Ico, "ICO", false),
            ("heic", MagickFormat.Heic, "HEIC", true)
        };
        foreach (var item in cases)
        {
            var path = Path.Combine(FixtureDirectory, $"first-page.{item.Extension}");
            try
            {
                if (item.Format == MagickFormat.Tiff)
                {
                    using var pages = new MagickImageCollection();
                    pages.Add(new MagickImage(MagickColors.Lime, 8, 6));
                    pages.Add(new MagickImage(MagickColors.Magenta, 8, 6));
                    pages.Write(path, item.Format);
                }
                else
                {
                    using var image = SvgImage("<svg xmlns='http://www.w3.org/2000/svg' width='8' height='6'><rect width='8' height='6' fill='#00ff00'/></svg>");
                    image.Write(path, item.Format);
                }

                var document = ImageLoader.Load(path);
                Equal(1, document.Frames.Count, $"{item.Name} first page only");
                Check(document.Width > 0 && document.Height > 0, $"{item.Name} dimensions");
                PixelNear(document.Frames[0], 2, 2, 0, 255, 0, 255, 45, $"{item.Name} first page color");
                Pass(item.Name);
            }
            catch (Exception ex) when (item.Optional && IsCodecUnavailable(ex))
            {
                _skipped++;
                Console.WriteLine($"SKIP: {item.Name} encoder unavailable ({ex.GetType().Name})");
            }
        }
    }

    private static void TestCorruptFile()
    {
        var path = Path.Combine(FixtureDirectory, "corrupt.png");
        File.WriteAllBytes(path, [0, 1, 2, 3, 4, 5, 6, 7]);
        Throws<Exception>(() => ImageLoader.Load(path), "corrupt image throws");
    }

    private static void TestPixelLimit()
    {
        var path = Path.Combine(FixtureDirectory, "over-100-million.png");
        using (var image = SvgImage("<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'><rect width='1' height='1' fill='red'/></svg>"))
            image.Write(path, MagickFormat.Png);
        SetPngDimensions(path, 10_001, 10_000);
        Throws<InvalidDataException>(() => ImageLoader.Load(path), "100-million-pixel image rejected before decode");
    }

    private static void TestAnimationFrameLimit()
    {
        var path = Path.Combine(FixtureDirectory, "over-200-frames.gif");
        var oldListLimit = ResourceLimits.ListLength;
        ResourceLimits.ListLength = 512;
        try { WriteGif(path, 201); }
        finally { ResourceLimits.ListLength = oldListLimit; }
        ThrowsAny(() => ImageLoader.Load(path), "201-frame animation rejected");
        var thumbnail = ImageLoader.LoadThumbnail(path, 160);
        Check(thumbnail.PixelWidth <= 160 && thumbnail.PixelHeight <= 160, "201-frame animation thumbnail is bounded");
        PixelNear(thumbnail, thumbnail.PixelWidth / 2, thumbnail.PixelHeight / 2, 255, 0, 0, 255, 0, "201-frame thumbnail reads only the first frame");
    }

    private static void WriteGif(string path, int frameCount)
    {
        using var images = new MagickImageCollection();
        for (var i = 0; i < frameCount; i++)
        {
            if (frameCount == 3)
            {
                var svg = i switch
                {
                    0 => "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect width='8' height='8' fill='#ff0000'/></svg>",
                    1 => "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='0' y='0' width='4' height='4' fill='#00ff00'/></svg>",
                    _ => "<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8'><rect x='4' y='4' width='4' height='4' fill='#0000ff'/></svg>"
                };
                var partialFrame = SvgImage(svg, transparent: true);
                partialFrame.Depth = 8;
                partialFrame.ColorType = ColorType.TrueColorAlpha;
                partialFrame.AnimationDelay = (uint)(5 + (i % 3) * 2);
                partialFrame.AnimationTicksPerSecond = 100;
                partialFrame.GifDisposeMethod = GifDisposeMethod.Background;
                images.Add(partialFrame);
                continue;
            }
            var color = (i % 3) switch { 0 => MagickColors.Red, 1 => MagickColors.Lime, _ => MagickColors.Blue };
            var frame = new MagickImage(color, 8, 8)
            {
                AnimationDelay = (uint)(5 + (i % 3) * 2),
                AnimationTicksPerSecond = 100,
                GifDisposeMethod = GifDisposeMethod.Background
            };
            images.Add(frame);
        }
        images.Write(path, MagickFormat.Gif);
    }

    private static MagickImage SvgImage(string svg, bool transparent = false)
    {
        var data = System.Text.Encoding.UTF8.GetBytes(svg);
        if (!transparent) return new MagickImage(data);
        return new MagickImage(data, new MagickReadSettings { BackgroundColor = MagickColors.Transparent });
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var result = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(result, bitmap.PixelWidth * 4, 0);
        return result;
    }

    private static void PixelNear(BitmapSource bitmap, int x, int y, int red, int green, int blue, int alpha, int tolerance, string label)
    {
        var pixels = Pixels(bitmap);
        var offset = (y * bitmap.PixelWidth + x) * 4;
        Near(pixels[offset + 2], red, tolerance, $"{label} red");
        Near(pixels[offset + 1], green, tolerance, $"{label} green");
        Near(pixels[offset], blue, tolerance, $"{label} blue");
        Near(pixels[offset + 3], alpha, tolerance, $"{label} alpha");
    }

    private static void Near(int actual, int expected, int tolerance, string label)
    {
        if (Math.Abs(actual - expected) > tolerance) throw new Exception($"{label}: expected {expected}±{tolerance}, got {actual}");
    }

    private static void Equal<T>(T expected, T actual, string label) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{label}: expected {expected}, got {actual}");
        Pass(label);
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Pass(label);
    }

    private static void Throws<TException>(Action action, string label) where TException : Exception
    {
        try { action(); }
        catch (TException) { Pass(label); return; }
        throw new Exception($"{label}: expected {typeof(TException).Name}");
    }

    private static void ThrowsAny(Action action, string label)
    {
        try { action(); }
        catch (Exception) { Pass(label); return; }
        throw new Exception($"{label}: expected an exception");
    }

    private static void Pass(string label)
    {
        _passed++;
        Console.WriteLine($"PASS: {label}");
    }

    private static void Run(string label, Action test)
    {
        try { test(); }
        catch (Exception ex)
        {
            _failed++;
            Console.Error.WriteLine($"FAIL: {label}: {ex}");
        }
    }

    private static bool IsCodecUnavailable(Exception ex) => ex is MagickException or NotSupportedException or InvalidOperationException;

    private static void SetPngDimensions(string path, uint width, uint height)
    {
        var data = File.ReadAllBytes(path);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20, 4), height);
        var crc = PngCrc32(data.AsSpan(12, 17));
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(29, 4), crc);
        File.WriteAllBytes(path, data);
    }

    private static void InsertExifOrientation(string path, ushort orientation)
    {
        var jpeg = File.ReadAllBytes(path);
        // APP1 payload: "Exif\0\0" followed by a minimal little-endian TIFF IFD containing tag 0x0112.
        var exif = new byte[]
        {
            0xff, 0xe1, 0x00, 0x22, 0x45, 0x78, 0x69, 0x66, 0x00, 0x00,
            0x49, 0x49, 0x2a, 0x00, 0x08, 0x00, 0x00, 0x00, 0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,
            (byte)(orientation & 0xff), (byte)(orientation >> 8), 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };
        var output = new byte[jpeg.Length + exif.Length];
        Buffer.BlockCopy(jpeg, 0, output, 0, 2);
        Buffer.BlockCopy(exif, 0, output, 2, exif.Length);
        Buffer.BlockCopy(jpeg, 2, output, 2 + exif.Length, jpeg.Length - 2);
        File.WriteAllBytes(path, output);
    }

    private static uint PngCrc32(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xffffffff;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }
}
