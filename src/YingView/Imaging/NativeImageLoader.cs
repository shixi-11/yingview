using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YingView.Imaging;

/// <summary>Fast full-resolution decode for ordinary still images; specialized files retain the color/animation pipeline.</summary>
internal static class NativeImageLoader
{
    internal static ImageDocument? TryLoad(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp")) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (extension == ".png" && !IsOrdinaryPng(stream)) return null;
        try
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if ((extension == ".png" && decoder is not PngBitmapDecoder)
                || (extension is ".jpg" or ".jpeg" && decoder is not JpegBitmapDecoder)
                || (extension == ".bmp" && decoder is not BmpBitmapDecoder)) return null;
            if (decoder.Frames.Count != 1) return null;
            var frame = decoder.Frames[0];
            var width = frame.PixelWidth; var height = frame.PixelHeight;
            if ((long)width * height > 100_000_000) throw new InvalidDataException("Image exceeds the 100-million-pixel limit.");
            // Keep embedded color profiles, CMYK and EXIF transforms on the established decoder.
            if (frame.ColorContexts is { Count: > 0 } || frame.Format == PixelFormats.Cmyk32 || frame.Format.BitsPerPixel > 32) return null;
            if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("/app1/ifd/{ushort=274}") is { } orientation && Convert.ToUInt16(orientation) is not (0 or 1)) return null;
            BitmapSource source = frame.Format == PixelFormats.Bgra32 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            var stride = checked(width * 4);
            var pixels = new byte[checked(stride * height)];
            source.CopyPixels(pixels, stride, 0);
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();
            var format = extension is ".jpg" or ".jpeg" ? "JPEG" : extension[1..].ToUpperInvariant();
            return new ImageDocument([bitmap], [20], width, height, format, (long)stride * height, FilePath: Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or COMException)
        {
            return null; // A codec limitation must not remove support supplied by the fallback decoder.
        }
    }

    private static bool IsOrdinaryPng(Stream stream)
    {
        Span<byte> header = stackalloc byte[8];
        Span<byte> ihdr = stackalloc byte[13];
        if (stream.Read(header) != 8 || !header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        while (stream.Position <= stream.Length - 12)
        {
            stream.ReadExactly(header);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (length > stream.Length - stream.Position - 4) return false;
            var kind = header[4..];
            // APNG control precedes IDAT. Profile/EXIF/high-depth PNGs keep their existing semantics.
            if (kind.SequenceEqual("acTL"u8) || kind.SequenceEqual("iCCP"u8) || kind.SequenceEqual("eXIf"u8)) return false;
            if (kind.SequenceEqual("IDAT"u8)) { stream.Position = 0; return true; }
            if (kind.SequenceEqual("IHDR"u8))
            {
                if (length != 13) return false;
                stream.ReadExactly(ihdr);
                if (ihdr[8] > 8) return false;
                stream.Seek(4, SeekOrigin.Current);
            }
            else stream.Seek(length + 4L, SeekOrigin.Current);
        }
        return false;
    }
}
