using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "YingView", "YingView.csproj"))) root = root.Parent;
        if (root is null) throw new DirectoryNotFoundException("Run inside the YingView checkout.");
        var assets = Path.Combine(root.FullName, "src", "YingView", "Assets");
        var preview = Path.Combine(root.FullName, "output", "branding"); Directory.CreateDirectory(preview);
        foreach (var language in new[] { "auto", "zh-CN", "en-US" })
        {
            var visual = Draw(language);
            var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
            var frames = sizes.Select(s => Png(visual, s)).ToArray();
            var name = language == "auto" ? "yingview.ico" : $"yingview-{language}.ico";
            using var file = File.Create(Path.Combine(assets, name)); using var writer = new BinaryWriter(file);
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            uint offset = (uint)(6 + 16 * sizes.Length);
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write((uint)frames[i].Length); writer.Write(offset); offset += (uint)frames[i].Length;
            }
            foreach (var bytes in frames) writer.Write(bytes);
            File.WriteAllBytes(Path.Combine(preview, $"icon-{language}.png"), Png(visual, 256));
        }
        Console.WriteLine("Built seven-size Windows icons and previews: " + preview);
    }
    private static DrawingVisual Draw(string language)
    {
        var visual = new DrawingVisual(); using var dc = visual.RenderOpen();
        var background = new LinearGradientBrush(Color.FromRgb(30, 58, 58), Color.FromRgb(14, 31, 38), 55);
        var accent = new LinearGradientBrush(Color.FromRgb(183, 234, 217), Color.FromRgb(108, 195, 183), 70);
        dc.DrawRoundedRectangle(background, new Pen(new SolidColorBrush(Color.FromArgb(40, 205, 240, 227)), 1), new Rect(8, 8, 240, 240), 52, 52);
        if (language == "auto")
        {
            var pen = new Pen(accent, 10) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawRoundedRectangle(null, pen, new Rect(47, 58, 162, 140), 15, 15);
            dc.DrawEllipse(accent, null, new Point(165, 96), 11, 11);
            dc.DrawGeometry(null, pen, Geometry.Parse("M 57,178 L 107,122 L 145,158 L 171,135 L 199,171"));
        }
        else
        {
            var zh = language == "zh-CN";
            var text = new FormattedText(zh ? "映" : "Y", CultureInfo.GetCultureInfo(language), FlowDirection.LeftToRight,
                new Typeface(new FontFamily(zh ? "Microsoft YaHei UI" : "Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), zh ? 158 : 176, accent, 1);
            dc.DrawText(text, new Point((256 - text.Width) / 2, (256 - text.Height) / 2 - (zh ? 5 : 2)));
        }
        return visual;
    }
    private static byte[] Png(DrawingVisual visual, int size)
    {
        var bitmap = new RenderTargetBitmap(size, size, 96d * size / 256, 96d * size / 256, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
}
