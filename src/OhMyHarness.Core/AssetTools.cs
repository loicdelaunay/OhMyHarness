using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public static class AssetTools
{
    public const string SkillId = "asset_generator";
    public const string Instructions = "Create drawings with asset_create; set pixel_size to use a logical pixel-art grid. Inspect with asset_inspect, align with asset_guides (exact center and bounding boxes), edit with asset_edit, visually check with asset_capture, and export with asset_export. GUI shows edits live in Tools > Assets. Drawings persist per conversation and remain editable. Coordinates originate at top-left; pixel operations use logical cells, while shapes use canvas pixels. Colors support full RGB/alpha (#RRGGBB or #RRGGBBAA); none erases a pixel. Use frame_add to create animation frames, frame_id to edit one, and frame_duration to set its timing. Add all frames before exporting animated SVG, GIF or numbered PNG frame ZIP. SVG animation uses discrete visibility frames and loops. Capture a specific frame with frame_id and set guides=true or grid=true to reveal non-exported alignment overlays. Use asset_guides for numeric centering, capture after meaningful changes, and expected_revision to avoid overwriting edits. A shape upsert replaces that shape completely. Exports stay in the conversation's managed assets/exports folder. JPEG requires an opaque background; GIF uses scale 1 and has a 4-million-pixel total cap. Never claim to have seen a capture if the model cannot interpret images. Respect tool permissions; unavailable in sandbox and to subagents; Plan permits inspection, guides and capture only.";
    public sealed record Result(string Text, Attachment? Image = null, AssetDocument? Document = null);
    public static bool Handles(string name) => name is "asset_create" or "asset_inspect" or "asset_edit" or "asset_capture" or "asset_export" or "asset_guides";
    public static AssetWorkspace Workspace(ConversationSession run) => new(run.Db.Database.GetDbConnection().DataSource, run.Chat.Id);
    public static void AddDefinitions(JsonArray tools, string skills)
    {
        if (!Skills.Enabled(skills, SkillId)) return;
        JsonObject Text(string description = "") => new() { ["type"] = "string", ["description"] = description };
        JsonObject Number() => new() { ["type"] = "number" };
        JsonObject Integer() => new() { ["type"] = "integer" };
        JsonObject Bool() => new() { ["type"] = "boolean" };
        JsonObject Choice(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()) };
        JsonObject Object(JsonObject properties, params string[] required) => new() { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false,
            ["required"] = new JsonArray(required.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()) };
        void Add(string name, string description, JsonObject properties, params string[] required) => tools.Add(new JsonObject { ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = name, ["description"] = description, ["parameters"] = Object(properties, required) } });
        var shape = Object(new() { ["id"] = Text(), ["type"] = Choice("rect","ellipse","circle","line","polygon","polyline","path","text"),
            ["x"] = Number(), ["y"] = Number(), ["width"] = Number(), ["height"] = Number(), ["radius"] = Number(), ["x2"] = Number(), ["y2"] = Number(),
            ["fill"] = Text("#RRGGBB, #RRGGBBAA, none"), ["stroke"] = Text(), ["strokeWidth"] = Number(), ["opacity"] = Number(), ["rotation"] = Number(),
            ["path"] = Text("SVG d path data only"), ["text"] = Text(), ["fontSize"] = Number(), ["fontFamily"] = Text(), ["bold"] = Bool(),
            ["points"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "array", ["items"] = Number(), ["minItems"] = 2, ["maxItems"] = 2 } } }, "id", "type");
        Add("asset_create", "Create a persistent canvas after approval; pixel_size (1–64) enables logical pixel-art cells and must divide width/height. Returns asset_id/revision and layer-1.", new() { ["name"] = Text(), ["width"] = Integer(), ["height"] = Integer(), ["background"] = Text(), ["pixel_size"] = Integer() }, "name");
        Add("asset_inspect", "Read a drawing's editable scene and revision; without asset_id lists this conversation's assets. No network.", new() { ["asset_id"] = Text() });
        Add("asset_guides", "Read canvas center, thirds and each visible shape/pixel bounding box in exact canvas coordinates, including offsets to center. Use these guides to align artwork before editing. Read-only.", new() { ["asset_id"] = Text(), ["frame_id"] = Text("Optional animation frame ID") }, "asset_id");
        Add("asset_edit", "Apply 1–100 operations atomically after approval. expected_revision must match. Use pixel/pixel_rect/pixel_line for pixel art (logical cell coordinates); frame_add/frame_delete/frame_duration for animation; frame_id on layer, shape or pixel operations edits that frame instead of the base scene. Layers/shapes paint back to front. Limits: 32 frames, 64 layers per frame, 2 MB scene.", new() {
            ["asset_id"] = Text(), ["expected_revision"] = Integer(), ["operations"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 100,
                ["items"] = Object(new() { ["action"] = Choice("layer","delete_layer","move_layer","shape","delete_shape","move_shape","canvas","pixel","pixel_rect","pixel_line","erase_pixel","frame_add","frame_delete","frame_duration"),
                    ["layer_id"] = Text(), ["shape_id"] = Text(), ["frame_id"] = Text(), ["source_frame_id"] = Text(), ["name"] = Text(), ["visible"] = Bool(), ["opacity"] = Number(), ["index"] = Integer(),
                    ["width"] = Integer(), ["height"] = Integer(), ["pixel_size"] = Integer(), ["x"] = Integer(), ["y"] = Integer(), ["x2"] = Integer(), ["y2"] = Integer(), ["color"] = Text(), ["duration_ms"] = Integer(),
                    ["background"] = Text(), ["shape"] = shape }, "action") } }, "asset_id","expected_revision","operations");
        Add("asset_capture", "Capture the asset or a chosen animation frame as PNG for visual inspection. guides=true overlays center crosshair and grid=true overlays pixel cells on the capture only. max_size 64–2048; reports scale and coordinates.", new() { ["asset_id"] = Text(), ["frame_id"] = Text(), ["max_size"] = Integer(), ["guides"] = Bool(), ["grid"] = Bool() }, "asset_id");
        Add("asset_export", "Export SVG, PNG, WebP, JPEG, PDF, animated SVG (svg-animated), GIF, or numbered PNG frames ZIP (frames). Animated formats use frame durations and repeat; transparent=true omits canvas background. GIF scale must be 1 and is limited to 4 million pixels across frames.", new() { ["asset_id"] = Text(), ["format"] = Choice("svg","png","webp","jpeg","pdf","svg-animated","gif","frames"), ["transparent"] = Bool(), ["background"] = Text(), ["scale"] = Number() }, "asset_id","format");
    }
    public static void Edit(AssetDocument doc, JsonArray operations)
    {
        if (operations.Count is < 1 or > 100) throw new ArgumentException("1–100 operations required.");
        foreach (var node in operations)
        {
            var op = node?.AsObject() ?? throw new ArgumentException("Invalid operation.");
            string S(string key) => op[key]?.GetValue<string>() ?? "";
            string action = S("action"), layerId = S("layer_id");
            if (action == "canvas")
            { doc.Name = op["name"]?.GetValue<string>() ?? doc.Name; doc.Width = op["width"]?.GetValue<int>() ?? doc.Width; doc.Height = op["height"]?.GetValue<int>() ?? doc.Height; doc.PixelSize = op["pixel_size"]?.GetValue<int>() ?? doc.PixelSize; doc.Background = op["background"]?.GetValue<string>() ?? doc.Background; continue; }
            if (action == "frame_add")
            {
                var id = S("frame_id"); AssetWorkspace.ValidId(id);
                if (doc.Frames.Any(f => f.Id == id)) throw new ArgumentException("Duplicate frame ID.");
                var source = S("source_frame_id");
                var sourceLayers = source.Length == 0 ? doc.Layers : doc.Frames.Single(f => f.Id == source).Layers;
                doc.Frames.Add(new AssetFrame { Id = id, DurationMs = op["duration_ms"]?.GetValue<int>() ?? 120,
                    Layers = JsonSerializer.Deserialize<List<AssetLayer>>(JsonSerializer.Serialize(sourceLayers, AssetDocument.Json), AssetDocument.Json)! });
                continue;
            }
            if (action == "frame_delete" || action == "frame_duration")
            {
                var frame = doc.Frames.Single(f => f.Id == S("frame_id"));
                if (action == "frame_delete") doc.Frames.Remove(frame); else frame.DurationMs = op["duration_ms"]?.GetValue<int>() ?? frame.DurationMs;
                continue;
            }
            var frameId = S("frame_id");
            var layers = frameId.Length == 0 ? doc.Layers : doc.Frames.Single(f => f.Id == frameId).Layers;
            AssetWorkspace.ValidId(layerId);
            var layer = layers.SingleOrDefault(l => l.Id == layerId);
            if (action == "layer")
            {
                if (layer == null) { layer = new() { Id = layerId, Name = layerId }; layers.Add(layer); }
                layer.Name = op["name"]?.GetValue<string>() ?? layer.Name; layer.Visible = op["visible"]?.GetValue<bool>() ?? layer.Visible; layer.Opacity = op["opacity"]?.GetValue<double>() ?? layer.Opacity; continue;
            }
            if (layer == null) throw new ArgumentException("Unknown layer: " + layerId);
            if (action == "delete_layer") { layers.Remove(layer); continue; }
            if (action == "move_layer") { Move(layers, layer, op["index"]?.GetValue<int>() ?? -1); continue; }
            if (action is "pixel" or "pixel_rect" or "pixel_line" or "erase_pixel")
            {
                int x = op["x"]?.GetValue<int>() ?? throw new ArgumentException("Pixel x required.");
                int y = op["y"]?.GetValue<int>() ?? throw new ArgumentException("Pixel y required.");
                string color = action == "erase_pixel" ? "none" : op["color"]?.GetValue<string>() ?? throw new ArgumentException("Pixel color required.");
                _ = AssetRenderer.Color(color);
                void Set(int px, int py)
                { if (px < 0 || py < 0 || px >= doc.Width / doc.PixelSize || py >= doc.Height / doc.PixelSize) throw new ArgumentException("Pixel outside canvas.");
                    layer.Pixels.RemoveAll(p => p.X == px && p.Y == py); if (color != "none") layer.Pixels.Add(new AssetPixel { X = px, Y = py, Color = color }); }
                if (action == "pixel_rect")
                { int w = op["width"]?.GetValue<int>() ?? 1, h = op["height"]?.GetValue<int>() ?? 1;
                  if (w < 1 || h < 1 || (long)w*h > 4096) throw new ArgumentException("Pixel rectangle must contain 1–4096 cells.");
                  for (int yy=y; yy<y+h; yy++) for (int xx=x; xx<x+w; xx++) Set(xx,yy); }
                else if (action == "pixel_line")
                { int x2=op["x2"]?.GetValue<int>() ?? x, y2=op["y2"]?.GetValue<int>() ?? y;
                  if (Math.Abs(x2-x)+Math.Abs(y2-y)>4096) throw new ArgumentException("Pixel line too long.");
                  int dx=Math.Abs(x2-x), sx=x<x2?1:-1, dy=-Math.Abs(y2-y), sy=y<y2?1:-1, error=dx+dy;
                  while (true) { Set(x,y); if (x==x2 && y==y2) break; int twice=2*error; if (twice>=dy) {error+=dy;x+=sx;} if(twice<=dx){error+=dx;y+=sy;} } }
                else Set(x,y);
                continue;
            }
            if (action == "shape")
            {
                var shape = op["shape"]?.Deserialize<AssetShape>(AssetDocument.Json) ?? throw new ArgumentException("shape required."); shape.Validate();
                var index = layer.Shapes.FindIndex(s => s.Id == shape.Id); if (index < 0) layer.Shapes.Add(shape); else layer.Shapes[index] = shape; continue;
            }
            var item = layer.Shapes.SingleOrDefault(s => s.Id == S("shape_id")) ?? throw new ArgumentException("Unknown shape.");
            if (action == "delete_shape") layer.Shapes.Remove(item);
            else if (action == "move_shape") Move(layer.Shapes, item, op["index"]?.GetValue<int>() ?? -1);
            else throw new ArgumentException("Unknown asset operation.");
        }
    }
    static void Move<T>(List<T> list, T item, int index)
    { if (index < 0 || index >= list.Count) throw new ArgumentException("Invalid layer/shape index."); list.Remove(item); list.Insert(index, item); }
    public static async Task<Result> CallAsync(ConversationSession run, string name, JsonObject args, Func<CancellationToken, Task<string>> skills,
        Func<string,string,string,CancellationToken,Task<bool>> approve, CancellationToken ct)
    {
        async Task Demand()
        { AgentPolicy.Demand(run.Chat.ExecutionMode, name); SandboxWorkspace.Demand(run.Chat.SandboxEnabled,name); if (!Skills.Enabled(await skills(ct),SkillId)) throw new UnauthorizedAccessException("Générateur d’assets désactivé."); ct.ThrowIfCancellationRequested(); }
        await Demand(); var workspace = Workspace(run);
        string id = args["asset_id"]?.GetValue<string>() ?? "";
        if (name == "asset_inspect") return new(id.Length == 0
            ? JsonSerializer.Serialize((await workspace.ListAsync(ct)).Select(d => new { asset_id = d.Id, d.Name, d.Width, d.Height, d.Revision }),AssetDocument.Json)
            : JsonSerializer.Serialize(await workspace.ReadAsync(id,ct),AssetDocument.Json));
        if (name == "asset_guides") return new(AssetGuides.Describe(await workspace.ReadAsync(id, ct), args["frame_id"]?.GetValue<string>()));
        if (!Handles(name)) throw new ArgumentException("Unknown asset tool.");
        if (!await approve("assets|" + run.Chat.Id + "|" + name, "Générateur d’assets / Asset generator", args.ToJsonString(), ct)) return new("Access denied; asset unchanged.");
        await Demand();
        AssetDocument doc;
        if (name == "asset_create") doc = await workspace.CreateAsync(args["name"]?.GetValue<string>() ?? "Asset", args["width"]?.GetValue<int>() ?? 512, args["height"]?.GetValue<int>() ?? 512, args["background"]?.GetValue<string>() ?? "none", args["pixel_size"]?.GetValue<int>() ?? 1, ct);
        else if (name == "asset_edit") doc = await workspace.UpdateAsync(id, args["expected_revision"]?.GetValue<int>() ?? -1, d => Edit(d, args["operations"]?.AsArray() ?? []), ct);
        else
        {
            doc = await workspace.ReadAsync(id,ct);
            if (name == "asset_capture")
            {
                int size = args["max_size"]?.GetValue<int>() ?? 1400;
                if (size is < 64 or > 2048) throw new ArgumentException("max_size: 64–2048.");
                var frameId = args["frame_id"]?.GetValue<string>();
                int frameIndex = frameId == null ? 0 : doc.Frames.FindIndex(f => f.Id == frameId);
                if (frameIndex < 0) throw new ArgumentException("Unknown frame ID.");
                var bytes = await Task.Run(() => AssetRenderer.Preview(doc,size,false,frameIndex,args["guides"]?.GetValue<bool>() ?? false,args["grid"]?.GetValue<bool>() ?? false),ct);
                if (bytes.Length > 8*1024*1024) throw new IOException("Capture exceeds 8 MB; lower max_size.");
                var scale=Math.Min(1,size/(float)Math.Max(doc.Width,doc.Height));
                return new(JsonSerializer.Serialize(new { asset_id=doc.Id, doc.Revision, frame_id=frameId, guides=args["guides"]?.GetValue<bool>()??false, grid=args["grid"]?.GetValue<bool>()??false,
                    doc.Width, doc.Height, scale, capture_width=(int)Math.Ceiling(doc.Width*scale),capture_height=(int)Math.Ceiling(doc.Height*scale),origin="top-left of artwork, no desktop coordinates" },AssetDocument.Json), new Attachment { Name=doc.Name+".png", Mime="image/png", Data=bytes },doc);
            }
            var path = await workspace.ExportAsync(doc,args["format"]?.GetValue<string>()??"svg",args["transparent"]?.GetValue<bool>()??false,args["background"]?.GetValue<string>(),args["scale"]?.GetValue<float>()??1,ct);
            return new(JsonSerializer.Serialize(new { path, asset_id=doc.Id, doc.Revision },AssetDocument.Json),Document:doc);
        }
        return new(JsonSerializer.Serialize(new { asset_id=doc.Id,doc.Revision,document=doc },AssetDocument.Json),Document:doc);
    }
}
