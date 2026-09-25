using System.Text.Json;

namespace OhMyHarness.Core;

public static class AssetGuides
{
    public static string Describe(AssetDocument document, string? frameId = null)
    {
        document.Validate();
        var index = frameId == null ? 0 : document.Frames.FindIndex(f => f.Id == frameId);
        if (index < 0) throw new ArgumentException("Unknown frame ID.");
        var scene = AssetAnimation.FrameScene(document, index);
        var bounds = new List<object>();
        foreach (var layer in scene.Layers.Where(l => l.Visible))
        {
            foreach (var s in layer.Shapes)
            {
                float x = s.Type == "circle" ? s.X - s.Radius : s.Type == "line" ? Math.Min(s.X,s.X2) : s.X;
                float y = s.Type == "circle" ? s.Y - s.Radius : s.Type == "line" ? Math.Min(s.Y,s.Y2) : s.Y;
                float w = s.Type == "circle" ? s.Radius * 2 : s.Type == "line" ? Math.Abs(s.X2-s.X) : s.Width;
                float h = s.Type == "circle" ? s.Radius * 2 : s.Type == "line" ? Math.Abs(s.Y2-s.Y) : s.Height;
                if (s.Type is "polygon" or "polyline" && s.Points.Count > 0)
                { x=s.Points.Min(p=>p[0]); y=s.Points.Min(p=>p[1]); w=s.Points.Max(p=>p[0])-x; h=s.Points.Max(p=>p[1])-y; }
                // Text/path/rotated geometry requires a visual capture for exact visible edges.
                bounds.Add(new { layer_id = layer.Id, shape_id = s.Id, x, y, width = w, height = h,
                    center_x = x+w/2, center_y = y+h/2, offset_to_canvas_center_x = document.Width/2f-(x+w/2),
                    offset_to_canvas_center_y = document.Height/2f-(y+h/2), approximate = s.Type is "text" or "path" || s.Rotation != 0 });
            }
            if (layer.Pixels.Count > 0)
            {
                int x=layer.Pixels.Min(p=>p.X)*document.PixelSize, y=layer.Pixels.Min(p=>p.Y)*document.PixelSize;
                int w=(layer.Pixels.Max(p=>p.X)+1)*document.PixelSize-x, h=(layer.Pixels.Max(p=>p.Y)+1)*document.PixelSize-y;
                bounds.Add(new { layer_id = layer.Id, pixels = layer.Pixels.Count, x, y, width = w, height = h,
                    center_x = x+w/2d, center_y = y+h/2d, offset_to_canvas_center_x = document.Width/2d-(x+w/2d),
                    offset_to_canvas_center_y = document.Height/2d-(y+h/2d), approximate = false });
            }
        }
        return JsonSerializer.Serialize(new { document.Id, document.Width, document.Height, document.PixelSize, frame_id = frameId,
            center = new { x = document.Width/2d, y = document.Height/2d },
            vertical_guides = new[] { document.Width/3d, document.Width/2d, document.Width*2d/3d },
            horizontal_guides = new[] { document.Height/3d, document.Height/2d, document.Height*2d/3d },
            bounds }, AssetDocument.Json);
    }
}
