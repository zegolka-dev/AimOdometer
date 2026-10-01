using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace AimOdometer.Tools.IconGen;

/// <summary>
/// Renders a simple SVG (rect, circle, path with fill/stroke) into a multi-size .ico and a 512 px PNG.
/// Usage: IconGen &lt;input.svg&gt; &lt;output.ico&gt; [output.png]
/// Only the SVG subset used by assets/logo.svg is supported; unknown elements fail loudly.
/// </summary>
internal static class Program
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            Console.Error.WriteLine("Usage: IconGen <input.svg> <output.ico> [output.png]");
            return 1;
        }

        var drawing = LoadSvg(args[0], out var viewBoxSize);
        var pngs = IconSizes.Select(size => RenderPng(drawing, viewBoxSize, size)).ToList();
        WriteIco(args[1], IconSizes, pngs);
        Console.WriteLine($"Wrote {args[1]} ({IconSizes.Length} sizes)");

        if (args.Length == 3)
        {
            File.WriteAllBytes(args[2], RenderPng(drawing, viewBoxSize, 512));
            Console.WriteLine($"Wrote {args[2]} (512 px)");
        }

        return 0;
    }

    private static DrawingGroup LoadSvg(string path, out double viewBoxSize)
    {
        var root = XDocument.Load(path).Root ?? throw new InvalidDataException("Empty SVG.");
        var viewBox = ((string?)root.Attribute("viewBox") ?? throw new InvalidDataException("viewBox is required."))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v, CultureInfo.InvariantCulture))
            .ToArray();
        if (viewBox.Length != 4 || viewBox[0] != 0 || viewBox[1] != 0 || viewBox[2] != viewBox[3])
        {
            throw new InvalidDataException("viewBox must be square and start at 0 0.");
        }

        viewBoxSize = viewBox[2];
        var group = new DrawingGroup();
        foreach (var element in root.Elements())
        {
            var geometry = element.Name.LocalName switch
            {
                "rect" => new RectangleGeometry(
                    new Rect(Num(element, "x"), Num(element, "y"), Num(element, "width"), Num(element, "height")),
                    Num(element, "rx"), Num(element, "rx")),
                "circle" => new EllipseGeometry(
                    new Point(Num(element, "cx"), Num(element, "cy")), Num(element, "r"), Num(element, "r")),
                "path" => Geometry.Parse((string?)element.Attribute("d") ?? throw new InvalidDataException("path without d")),
                _ => throw new InvalidDataException($"Unsupported SVG element <{element.Name.LocalName}>."),
            };

            group.Children.Add(new GeometryDrawing(Fill(element), Stroke(element), geometry));
        }

        group.Freeze();
        return group;
    }

    private static double Num(XElement element, string name) =>
        double.Parse((string?)element.Attribute(name) ?? "0", CultureInfo.InvariantCulture);

    private static SolidColorBrush? Fill(XElement element)
    {
        var fill = (string?)element.Attribute("fill") ?? "#000000";
        return fill == "none" ? null : ParseBrush(fill);
    }

    private static Pen? Stroke(XElement element)
    {
        var stroke = (string?)element.Attribute("stroke");
        if (stroke is null or "none")
        {
            return null;
        }

        var cap = (string?)element.Attribute("stroke-linecap") switch
        {
            "round" => PenLineCap.Round,
            "square" => PenLineCap.Square,
            _ => PenLineCap.Flat,
        };
        return new Pen(ParseBrush(stroke), Num(element, "stroke-width")) { StartLineCap = cap, EndLineCap = cap };
    }

    private static SolidColorBrush ParseBrush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private static byte[] RenderPng(Drawing drawing, double viewBoxSize, int size)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(size / viewBoxSize, size / viewBoxSize));
            context.DrawDrawing(drawing);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Writes an ICO container with PNG-compressed images (supported since Windows Vista).</summary>
    private static void WriteIco(string path, int[] sizes, List<byte[]> pngs)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // type: icon
        writer.Write((ushort)sizes.Length);

        var offset = 6 + (16 * sizes.Length);
        for (var i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); // 0 means 256
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)0); // palette colors
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // color planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(pngs[i].Length);
            writer.Write(offset);
            offset += pngs[i].Length;
        }

        foreach (var png in pngs)
        {
            writer.Write(png);
        }
    }
}
