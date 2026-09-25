using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using SkiaSharp;

namespace OhMyHarness.Core;

public sealed class AssetDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Asset";
    public int Width { get; set; } = 512;
    public int Height { get; set; } = 512;
    public string Background { get; set; } = "none";
    public int Revision { get; set; }
    public List<AssetLayer> Layers { get; set; } = [new()];
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public AssetDocument Clone() => JsonSerializer.Deserialize<AssetDocument>(JsonSerializer.Serialize(this, Json), Json)!;
    public void Validate()
    {
        AssetWorkspace.ValidId(Id);
        if (Name.Length is < 1 or > 120 || Width is < 1 or > 4096 || Height is < 1 or > 4096 || (long)Width * Height > 16777216) throw new ArgumentException("Canvas: name 1–120 characters, dimensions 1–4096 pixels.");
        _ = AssetRenderer.Color(Background);
        if (Layers.Count > 64 || Layers.Select(l => l.Id).Distinct().Count() != Layers.Count || Layers.Sum(l => l.Shapes.Count) > 2000) throw new ArgumentException("Maximum 64 uniquely named layers and 2000 shapes.");
        foreach (var layer in Layers)
        {
            AssetWorkspace.ValidId(layer.Id);
            if (layer.Name.Length > 120 || !double.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1 || layer.Shapes.Select(s => s.Id).Distinct().Count() != layer.Shapes.Count) throw new ArgumentException("Invalid layer name, opacity or duplicate shape IDs.");
            foreach (var shape in layer.Shapes) shape.Validate();
        }
        if (JsonSerializer.SerializeToUtf8Bytes(this, Json).Length > 2_000_000) throw new ArgumentException("Asset document exceeds 2 MB.");
    }
}
public sealed class AssetLayer
{
    public string Id { get; set; } = "layer-1";
    public string Name { get; set; } = "Calque 1";
    public bool Visible { get; set; } = true;
    public double Opacity { get; set; } = 1;
    public List<AssetShape> Shapes { get; set; } = [];
}
public sealed class AssetShape
{
    public string Id { get; set; } = "shape-1";
    public string Type { get; set; } = "rect";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 100;
    public float Height { get; set; } = 100;
    public float Radius { get; set; }
    public float X2 { get; set; } = 100;
    public float Y2 { get; set; } = 100;
    public string Fill { get; set; } = "#4CC9F0";
    public string Stroke { get; set; } = "none";
    public float StrokeWidth { get; set; } = 1;
    public float Opacity { get; set; } = 1;
    public float Rotation { get; set; }
    public string Path { get; set; } = "";
    public List<float[]> Points { get; set; } = [];
    public string Text { get; set; } = "";
    public float FontSize { get; set; } = 24;
    public string FontFamily { get; set; } = "Arial";
    public bool Bold { get; set; }
    public void Validate()
    {
        AssetWorkspace.ValidId(Id);
        if (Type is not ("rect" or "ellipse" or "circle" or "line" or "polygon" or "polyline" or "path" or "text")) throw new ArgumentException("Unsupported SVG shape.");
        foreach (float value in new[] { X, Y, Width, Height, Radius, X2, Y2, StrokeWidth, Opacity, Rotation, FontSize })
            if (!float.IsFinite(value) || Math.Abs(value) > 32768) throw new ArgumentException("Shape coordinates must be finite, within ±32768.");
        if (Width < 0 || Height < 0 || Radius < 0 || StrokeWidth is < 0 or > 512 || Opacity is < 0 or > 1 || FontSize is < 1 or > 1024) throw new ArgumentException("Invalid shape size or opacity.");
        _ = AssetRenderer.Color(Fill); _ = AssetRenderer.Color(Stroke);
        if (Path.Length > 16000 || Text.Length > 4000 || FontFamily.Length > 120 || Points.Count > 2000 || Points.Any(p => p.Length != 2 || p.Any(n => !float.IsFinite(n) || Math.Abs(n) > 32768))) throw new ArgumentException("Shape content exceeds limits.");
        if (Type == "path") { using var parsed = SKPath.ParseSvgPathData(Path); if (parsed == null) throw new ArgumentException("Invalid SVG path data."); }
        if (Type is "polygon" or "polyline" && Points.Count < 2) throw new ArgumentException("At least two points required.");
    }
}

/// <summary>SVG serialization and offline raster/PDF rendering share one constrained vector scene.</summary>
public static class AssetRenderer
{
    public static SKColor Color(string value)
    {
        if (value is "none" or "transparent") return SKColors.Transparent;
        // SVG/CSS order is RRGGBBAA, unlike several native APIs' AARRGGBB convention.
        if (value.Length is not (7 or 9) || value[0] != '#' || value.Skip(1).Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("Color must be #RRGGBB, #RRGGBBAA, none or transparent.");
        return new(Convert.ToByte(value.Substring(1, 2), 16), Convert.ToByte(value.Substring(3, 2), 16), Convert.ToByte(value.Substring(5, 2), 16), value.Length == 9 ? Convert.ToByte(value.Substring(7, 2), 16) : (byte)255);
    }
    static string N(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
    static SKPaint Paint(string color, float opacity, bool stroke = false, float width = 1) => new() {
        Color = Color(color).WithAlpha((byte)Math.Round(Color(color).Alpha * opacity)), IsAntialias = true,
        Style = stroke ? SKPaintStyle.Stroke : SKPaintStyle.Fill, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    static SKPath ShapePath(AssetShape s)
    {
        if (s.Type == "path") return SKPath.ParseSvgPathData(s.Path) ?? throw new ArgumentException("Invalid path.");
        var p = new SKPath();
        switch (s.Type)
        {
            case "rect": p.AddRoundRect(new(s.X, s.Y, s.X + s.Width, s.Y + s.Height), s.Radius, s.Radius); break;
            case "ellipse": p.AddOval(new(s.X, s.Y, s.X + s.Width, s.Y + s.Height)); break;
            case "circle": p.AddCircle(s.X, s.Y, s.Radius); break;
            case "line": p.MoveTo(s.X, s.Y); p.LineTo(s.X2, s.Y2); break;
            case "polygon": case "polyline":
                if (s.Points.Count > 0) { p.MoveTo(s.Points[0][0], s.Points[0][1]); foreach (var pt in s.Points.Skip(1)) p.LineTo(pt[0], pt[1]); if (s.Type == "polygon") p.Close(); } break;
        }
        return p;
    }
    static void Draw(SKCanvas canvas, AssetDocument doc, bool transparent, string? background)
    {
        if (!transparent)
        { using var bg = Paint(background ?? doc.Background, 1); canvas.DrawRect(0, 0, doc.Width, doc.Height, bg); }
        foreach (var layer in doc.Layers.Where(l => l.Visible))
        {
            using var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * layer.Opacity)) };
            canvas.SaveLayer(layerPaint);
            foreach (var s in layer.Shapes)
            {
                canvas.Save(); canvas.RotateDegrees(s.Rotation, s.X, s.Y);
                using var opacityPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * s.Opacity)) };
                canvas.SaveLayer(opacityPaint);
                using var fill = Paint(s.Fill, 1); using var stroke = Paint(s.Stroke, 1, true, s.StrokeWidth);
                if (s.Type == "text")
                {
                    using var typeface = SKTypeface.FromFamilyName(s.FontFamily, s.Bold ? SKFontStyle.Bold : SKFontStyle.Normal);
                    using var font = new SKFont(typeface, s.FontSize);
                    if (fill.Color.Alpha > 0) canvas.DrawText(s.Text, s.X, s.Y, SKTextAlign.Left, font, fill);
                    if (stroke.Color.Alpha > 0 && s.StrokeWidth > 0) canvas.DrawText(s.Text, s.X, s.Y, SKTextAlign.Left, font, stroke);
                }
                else
                {
                    using var path = ShapePath(s);
                    if (fill.Color.Alpha > 0 && s.Type != "line") canvas.DrawPath(path, fill);
                    if (stroke.Color.Alpha > 0 && s.StrokeWidth > 0) canvas.DrawPath(path, stroke);
                }
                canvas.Restore(); canvas.Restore();
            }
            canvas.Restore();
        }
    }
    public static string Svg(AssetDocument doc, bool transparent = false, string? background = null)
    {
        doc.Validate(); if (background != null) _ = Color(background);
        XNamespace ns = "http://www.w3.org/2000/svg";
        var svg = new XElement(ns + "svg", new XAttribute("width", doc.Width), new XAttribute("height", doc.Height), new XAttribute("viewBox", $"0 0 {doc.Width} {doc.Height}"));
        svg.Add(new XElement(ns + "title", doc.Name));
        void Attr(XElement e, string key, object value) => e.SetAttributeValue(key, value is float f ? N(f) : value);
        void SetColor(XElement e, string key, string value)
        { var c = Color(value); Attr(e, key, c.Alpha == 0 ? "none" : $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}"); if (c.Alpha > 0 && c.Alpha < 255) Attr(e, key + "-opacity", N(c.Alpha / 255d)); }
        if (!transparent && Color(background ?? doc.Background).Alpha > 0)
        { var bg = new XElement(ns + "rect", new XAttribute("width", doc.Width), new XAttribute("height", doc.Height)); SetColor(bg, "fill", background ?? doc.Background); svg.Add(bg); }
        foreach (var l in doc.Layers)
        {
            var group = new XElement(ns + "g", new XAttribute("id", l.Id), new XAttribute("data-name", l.Name), new XAttribute("opacity", N(l.Opacity)));
            if (!l.Visible) group.SetAttributeValue("display", "none");
            foreach (var s in l.Shapes)
            {
                var e = new XElement(ns + s.Type, new XAttribute("id", l.Id + "--" + s.Id));
                SetColor(e, "fill", s.Type == "line" ? "none" : s.Fill); SetColor(e, "stroke", s.Stroke);
                Attr(e, "stroke-width", s.StrokeWidth); Attr(e, "stroke-linecap", "round"); Attr(e, "stroke-linejoin", "round"); Attr(e, "opacity", s.Opacity);
                if (s.Rotation != 0) Attr(e, "transform", $"rotate({N(s.Rotation)} {N(s.X)} {N(s.Y)})");
                switch (s.Type)
                {
                    case "rect": Attr(e,"x",s.X); Attr(e,"y",s.Y); Attr(e,"width",s.Width); Attr(e,"height",s.Height); Attr(e,"rx",s.Radius); break;
                    case "ellipse": Attr(e,"cx",s.X+s.Width/2); Attr(e,"cy",s.Y+s.Height/2); Attr(e,"rx",s.Width/2); Attr(e,"ry",s.Height/2); break;
                    case "circle": Attr(e,"cx",s.X); Attr(e,"cy",s.Y); Attr(e,"r",s.Radius); break;
                    case "line": Attr(e,"x1",s.X); Attr(e,"y1",s.Y); Attr(e,"x2",s.X2); Attr(e,"y2",s.Y2); break;
                    case "polygon": case "polyline": Attr(e,"points",string.Join(" ",s.Points.Select(p=>$"{N(p[0])},{N(p[1])}"))); break;
                    case "path": Attr(e,"d",s.Path); break;
                    case "text": Attr(e,"x",s.X); Attr(e,"y",s.Y); Attr(e,"font-size",s.FontSize); Attr(e,"font-family",s.FontFamily); Attr(e,"font-weight",s.Bold?"bold":"normal"); e.Value=s.Text; break;
                }
                group.Add(e);
            }
            svg.Add(group);
        }
        return svg.ToString();
    }
    public static byte[] Export(AssetDocument doc, string format, bool transparent = false, string? background = null, float scale = 1)
    {
        doc.Validate(); if (background != null) _ = Color(background);
        if (format is not ("svg" or "png" or "webp" or "jpeg" or "pdf")) throw new ArgumentException("Formats: svg, png, webp, jpeg, pdf.");
        if (!float.IsFinite(scale) || scale <= 0 || scale > 4) throw new ArgumentException("Scale: >0 to 4.");
        int w = Math.Max(1, (int)Math.Ceiling(doc.Width * scale)), h = Math.Max(1, (int)Math.Ceiling(doc.Height * scale));
        if ((long)w*h > 16777216 || w > 8192 || h > 8192) throw new ArgumentException("Export limited to 16 megapixels and 8192 pixels per side.");
        if (format == "svg") return Encoding.UTF8.GetBytes(Svg(doc, transparent, background));
        if (format == "jpeg" && (transparent || Color(background ?? doc.Background).Alpha != 255)) throw new ArgumentException("JPEG requires an opaque background. Choose PNG/WebP for transparency or background=#FFFFFF.");
        using var output = new MemoryStream();
        if (format == "pdf")
        {
            using var pdf = SKDocument.CreatePdf(output);
            var canvas = pdf.BeginPage(w, h); canvas.Scale(scale); Draw(canvas, doc, transparent, background); pdf.EndPage(); pdf.Close(); return output.ToArray();
        }
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.Scale(scale); Draw(surface.Canvas, doc, transparent, background);
        using var image = surface.Snapshot();
        using var data = image.Encode(format == "jpeg" ? SKEncodedImageFormat.Jpeg : format == "webp" ? SKEncodedImageFormat.Webp : SKEncodedImageFormat.Png, 100);
        if (data == null) throw new IOException("Image encoding failed.");
        return data.ToArray();
    }
    public static byte[] Preview(AssetDocument doc, int maxSize = 1400, bool checkerboard = false)
    {
        var png = Export(doc, "png", scale: Math.Min(1, maxSize/(float)Math.Max(doc.Width,doc.Height)));
        if (!checkerboard) return png;
        using var image = SKImage.FromEncodedData(png);
        using var surface = SKSurface.Create(new SKImageInfo(image.Width,image.Height));
        surface.Canvas.Clear(new SKColor(210,213,219));
        using var tile = new SKPaint { Color = new SKColor(236,238,242) };
        for(int y=0;y<image.Height;y+=16) for(int x=0;x<image.Width;x+=16)
            if ((x/16+y/16)%2==0) surface.Canvas.DrawRect(x,y,16,16,tile);
        surface.Canvas.DrawImage(image,0,0);
        using var snapshot = surface.Snapshot(); using var data = snapshot.Encode(SKEncodedImageFormat.Png,100); return data.ToArray();
    }
}
