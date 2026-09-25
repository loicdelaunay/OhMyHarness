using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SkiaSharp;

namespace OhMyHarness.Core;

public static class AssetAnimation
{
    public static AssetDocument FrameScene(AssetDocument document, int index)
    {
        var scene = document.Clone();
        if (scene.Frames.Count > 0)
        {
            if (index < 0 || index >= scene.Frames.Count) throw new ArgumentOutOfRangeException(nameof(index));
            scene.Layers = scene.Frames[index].Layers;
        }
        else if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
        scene.Frames = [];
        return scene;
    }

    public static byte[] Export(AssetDocument document, string format, bool transparent, string? background, float scale)
    {
        document.Validate();
        if (format == "svg-animated") return Encoding.UTF8.GetBytes(AnimatedSvg(document, transparent, background));
        if (format == "frames")
        {
            var sequenceCount = Math.Max(1, document.Frames.Count);
            if (!float.IsFinite(scale) || scale <= 0 || scale > 4 ||
                (long)Math.Ceiling(document.Width * scale) * (long)Math.Ceiling(document.Height * scale) * sequenceCount > 64_000_000)
                throw new ArgumentException("PNG frame sequence is limited to 64 million pixels total and scale >0 to 4.");
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                var count = sequenceCount;
                for (int i = 0; i < count; i++)
                {
                    var entry = zip.CreateEntry($"frame-{i + 1:D3}.png", CompressionLevel.Optimal);
                    using var stream = entry.Open();
                    var data = AssetRenderer.Export(FrameScene(document, i), "png", transparent, background, scale);
                    stream.Write(data);
                }
                var manifest = zip.CreateEntry("frames.json");
                using var writer = new StreamWriter(manifest.Open(), Encoding.UTF8);
                writer.Write(JsonSerializer.Serialize(new { width = document.Width, height = document.Height,
                    frames = Enumerable.Range(0, count).Select(i => new { file = $"frame-{i + 1:D3}.png", duration_ms = document.Frames.Count == 0 ? 120 : document.Frames[i].DurationMs }) }));
            }
            return output.ToArray();
        }
        if (format != "gif") throw new ArgumentException("Unknown animation format.");
        if (scale != 1) throw new ArgumentException("GIF export uses scale=1.");
        var frameCount = Math.Max(1, document.Frames.Count);
        if (document.Width > 1024 || document.Height > 1024 || (long)document.Width * document.Height * frameCount > 4_000_000)
            throw new ArgumentException("GIF limited to 1024 pixels per side and 4 million pixels across all frames. Use SVG animation or PNG frames for larger assets.");
        var frames = new List<byte[]>(frameCount);
        for (int i = 0; i < frameCount; i++) frames.Add(AssetRenderer.Export(FrameScene(document, i), "png", transparent, background));
        return Gif(frames, document.Frames.Count == 0 ? [120] : document.Frames.Select(f => f.DurationMs).ToArray(), document.Width, document.Height);
    }

    public static string AnimatedSvg(AssetDocument document, bool transparent = false, string? background = null)
    {
        document.Validate();
        XNamespace ns = "http://www.w3.org/2000/svg";
        var count = Math.Max(1, document.Frames.Count);
        var total = document.Frames.Count == 0 ? 120 : document.Frames.Sum(f => f.DurationMs);
        var times = new int[count + 1];
        for (int i = 1; i <= count; i++) times[i] = times[i - 1] + (document.Frames.Count == 0 ? 120 : document.Frames[i - 1].DurationMs);
        var root = XElement.Parse(AssetRenderer.Svg(FrameScene(document, 0), transparent, background));
        root.Elements().Where(x => x.Name != ns + "title").Remove();
        for (int i = 0; i < count; i++)
        {
            var frame = XElement.Parse(AssetRenderer.Svg(FrameScene(document, i), transparent, background));
            var group = new XElement(ns + "g", new XAttribute("id", "frame-" + (i + 1)), new XAttribute("visibility", i == 0 ? "visible" : "hidden"));
            foreach (var element in frame.Elements().Where(x => x.Name != ns + "title"))
            {
                var copy = new XElement(element);
                foreach (var identified in copy.DescendantsAndSelf().Where(x => x.Attribute("id") != null))
                    identified.SetAttributeValue("id", $"frame-{i + 1}--{identified.Attribute("id")!.Value}");
                group.Add(copy);
            }
            if (count > 1)
            {
                var values = Enumerable.Range(0, count + 1).Select(t => t % count == i ? "visible" : "hidden");
                group.Add(new XElement(ns + "animate", new XAttribute("attributeName", "visibility"), new XAttribute("values", string.Join(';', values)),
                    new XAttribute("keyTimes", string.Join(';', times.Select(t => (t / (double)total).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)))),
                    new XAttribute("dur", total + "ms"), new XAttribute("calcMode", "discrete"), new XAttribute("repeatCount", "indefinite")));
            }
            root.Add(group);
        }
        return root.ToString();
    }

    static byte[] Gif(IReadOnlyList<byte[]> frames, IReadOnlyList<int> delays, int width, int height)
    {
        using var output = new MemoryStream();
        void B(params byte[] bytes) => output.Write(bytes);
        void W(int value) { B((byte)value, (byte)(value >> 8)); }
        output.Write(Encoding.ASCII.GetBytes("GIF89a")); W(width); W(height); B(0xF7, 0, 0);
        for (int n = 0; n < 256; n++)
        { int r = (n >> 5) & 7, g = (n >> 2) & 7, b = n & 3; B((byte)(r * 255 / 7), (byte)(g * 255 / 7), (byte)(b * 255 / 3)); }
        B(0x21, 0xFF, 11); output.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0")); B(3, 1, 0, 0, 0);
        for (int frame = 0; frame < frames.Count; frame++)
        {
            using var bitmap = SKBitmap.Decode(frames[frame]) ?? throw new IOException("Cannot decode animation frame.");
            B(0x21, 0xF9, 4, 0x09); W(Math.Max(2, (int)Math.Round(delays[frame] / 10d))); B(0, 0);
            B(0x2C); W(0); W(0); W(width); W(height); B(0); B(8);
            var packed = new MemoryStream(); int pending = 0, bits = 0;
            void Code(int value) { pending |= value << bits; bits += 9; while (bits >= 8) { packed.WriteByte((byte)pending); pending >>= 8; bits -= 8; } }
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                int index = color.Alpha < 128 ? 0 : Math.Max(1, ((color.Red >> 5) << 5) | ((color.Green >> 5) << 2) | (color.Blue >> 6));
                Code(256); Code(index);
            }
            Code(257); if (bits > 0) packed.WriteByte((byte)pending);
            var raw = packed.ToArray();
            for (int offset = 0; offset < raw.Length; offset += 255) { int length = Math.Min(255, raw.Length - offset); B((byte)length); output.Write(raw, offset, length); }
            B(0);
        }
        B(0x3B); return output.ToArray();
    }
}
