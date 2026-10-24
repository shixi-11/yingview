using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
    [STAThread]
    private static void Main()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "YingView", "YingView.csproj"))) root = root.Parent;
        if (root is null) throw new DirectoryNotFoundException("Run inside the YingView checkout.");
        var assets = Path.Combine(root.FullName, "src", "YingView", "Assets");
        var preview = Path.Combine(root.FullName, "output", "branding"); Directory.CreateDirectory(preview);
        var svg = XDocument.Load(Path.Combine(assets, "app-icon.svg"));
        var visual = Draw(svg);
        File.WriteAllBytes(Path.Combine(assets, "app-icon.png"), Png(visual, 1024));
        var frames = Sizes.Select(s => Png(visual, s)).ToArray();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)Sizes.Length);
            uint offset = (uint)(6 + 16 * Sizes.Length);
            for (int i = 0; i < Sizes.Length; i++)
            {
                writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i])); writer.Write((byte)(Sizes[i] == 256 ? 0 : Sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write((uint)frames[i].Length); writer.Write(offset); offset += (uint)frames[i].Length;
            }
            foreach (var bytes in frames) writer.Write(bytes);
        }
        // Stable resource names, one global graphical identity.
        foreach (var name in new[] { "yingview.ico", "yingview-zh-CN.ico", "yingview-en-US.ico" })
            File.WriteAllBytes(Path.Combine(assets, name), stream.ToArray());
        foreach (var size in Sizes) File.WriteAllBytes(Path.Combine(preview, $"icon-{size}.png"), Png(visual, size));
        XNamespace ns = "http://www.w3.org/2000/svg";
        var mark = new XElement(ns + "svg", new XAttribute("viewBox", "0 0 256 256"), new XAttribute("fill", "currentColor"),
            svg.Root!.Elements(ns + "path").Select(p => new XElement(ns + "path", new XAttribute("d", p.Attribute("d")!.Value))));
        File.WriteAllText(Path.Combine(assets, "app-mark.svg"), mark.ToString());
        File.WriteAllBytes(Path.Combine(preview, "icon-review.png"), Png(Review(visual), 1024));
        Console.WriteLine("Built shared SVG-derived 1024px master, monochrome mark and ten-size Windows icons: " + preview);
    }
    private static DrawingVisual Draw(XDocument svg)
    {
        XNamespace ns = "http://www.w3.org/2000/svg";
        double N(XElement e, string a) => double.Parse(e.Attribute(a)!.Value, CultureInfo.InvariantCulture);
        var brushes = svg.Descendants(ns + "linearGradient").ToDictionary(e => e.Attribute("id")!.Value, e =>
        {
            var b = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(N(e, "x1"), N(e, "y1")), EndPoint = new Point(N(e, "x2"), N(e, "y2")) };
            foreach (var s in e.Elements(ns + "stop")) b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(s.Attribute("stop-color")!.Value), N(s, "offset")));
            return b;
        });
        var visual = new DrawingVisual(); using var dc = visual.RenderOpen();
        foreach (var e in svg.Root!.Elements().Where(e => e.Name == ns + "rect" || e.Name == ns + "path"))
        {
            var fill = e.Attribute("fill")!.Value; var brush = brushes[fill[5..^1]];
            if (e.Name == ns + "rect") dc.DrawRoundedRectangle(brush, null, new Rect(N(e, "x"), N(e, "y"), N(e, "width"), N(e, "height")), N(e, "rx"), N(e, "rx"));
            else dc.DrawGeometry(brush, null, Geometry.Parse(e.Attribute("d")!.Value));
        }
        return visual;
    }
    private static DrawingVisual Review(DrawingVisual icon)
    {
        var visual = new DrawingVisual(); using var dc = visual.RenderOpen();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 256, 128));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(22, 27, 35)), null, new Rect(0, 128, 256, 128));
        foreach (var y in new[] { 0d, 128d })
        {
            dc.PushTransform(new TranslateTransform(10, y + 8)); dc.PushTransform(new ScaleTransform(.37, .37));
            dc.DrawDrawing(icon.Drawing); dc.Pop(); dc.Pop();
            double x = 116;
            foreach (var size in new[] { 16, 24, 32, 48 })
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = new MemoryStream(Png(icon, size)); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit();
                dc.DrawImage(bitmap, new Rect(x, y + 35, size / 4d, size / 4d));
                dc.DrawText(new FormattedText(size.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 3.5, y == 0 ? Brushes.Black : Brushes.White, 1), new Point(x, y + 51));
                x += 32;
            }
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
