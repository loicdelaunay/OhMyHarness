using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public static class AssetTools
{
    public const string SkillId = "asset_generator";
    public const string Instructions = "Create vector drawings with asset_create, inspect them with asset_inspect (no asset_id lists this conversation's drawings), edit them with asset_edit, verify visually with asset_capture, and export with asset_export. GUI shows edits live in Tools > Assets. Drawings persist per conversation beside the database and remain editable. Start with canvas dimensions and layers; add shapes in small batches and capture after meaningful changes. Coordinates originate at the top-left; x/y is top-left for rectangles/ellipses, center for circles, start for lines, baseline for text; paths/points use absolute canvas coordinates. Rotation is around shape x/y. Colors support the full RGB palette and alpha: #RRGGBB or #RRGGBBAA; none removes paint. Layers and shapes paint from first (back) to last (front). Use path for arbitrary SVG curves, never markup/scripts/remote resources. Use expected_revision from the last read/update to avoid overwriting user edits. A shape upsert replaces that shape completely (omitted properties return to defaults); no pixels are drawn until the tool succeeds. Capture returns an image of the artwork only, not the desktop, and reports scale for coordinates; vision or the configured vision bridge is required to interpret it. Export SVG, PNG, WebP, JPEG or PDF; transparent=true omits the canvas background (drawn background shapes remain). JPEG requires an opaque background. Exports go to this conversation's managed assets/exports folder; report the returned path. Never claim to have seen a capture if your model cannot interpret images. Respect tool permissions; unavailable in sandbox and to subagents; Plan permits inspection/capture only.";
    public sealed record Result(string Text, Attachment? Image = null, AssetDocument? Document = null);
    public static bool Handles(string name) => name is "asset_create" or "asset_inspect" or "asset_edit" or "asset_capture" or "asset_export";
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
        Add("asset_create", "Create a persistent vector canvas after approval; returns asset_id/revision and an initial layer-1. Dimensions 1–4096. Defaults: 512×512, transparent background.", new() { ["name"] = Text(), ["width"] = Integer(), ["height"] = Integer(), ["background"] = Text() }, "name");
        Add("asset_inspect", "Read a drawing's editable scene and revision; without asset_id lists this conversation's assets. No network.", new() { ["asset_id"] = Text() });
        Add("asset_edit", "Apply 1–100 operations atomically after approval, then refresh the live canvas. expected_revision must match. layer upserts add/update properties, shape upserts replace the entire shape, delete removes a layer/shape, move sets its zero-based index (0=back). canvas changes name/size/background. Use IDs from inspect. Limits: 64 layers, 2000 shapes, 2 MB scene. Returns current scene.", new() {
            ["asset_id"] = Text(), ["expected_revision"] = Integer(), ["operations"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 100,
                ["items"] = Object(new() { ["action"] = Choice("layer","delete_layer","move_layer","shape","delete_shape","move_shape","canvas"),
                    ["layer_id"] = Text(), ["shape_id"] = Text(), ["name"] = Text(), ["visible"] = Bool(), ["opacity"] = Number(), ["index"] = Integer(),
                    ["width"] = Integer(), ["height"] = Integer(), ["background"] = Text(), ["shape"] = shape }, "action") } }, "asset_id","expected_revision","operations");
        Add("asset_capture", "Capture only this asset's rendered drawing as PNG for visual inspection, after transmission approval. max_size 64–2048 defaults to 1400. No desktop/browser permission needed. Transparent areas remain transparent; reports original dimensions and scale.", new() { ["asset_id"] = Text(), ["max_size"] = Integer() }, "asset_id");
        Add("asset_export", "Export to a new file in this conversation's assets/exports folder after approval; returns absolute path and revision. transparent=true removes the canvas background, not background shapes. JPEG requires an opaque background (e.g. #FFFFFF). PNG/WebP/SVG preserve alpha. PDF preserves vectors. scale >0 to 4 for raster/PDF, max 16 MP; default 1.", new() { ["asset_id"] = Text(), ["format"] = Choice("svg","png","webp","jpeg","pdf"), ["transparent"] = Bool(), ["background"] = Text(), ["scale"] = Number() }, "asset_id","format");
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
            { doc.Name = op["name"]?.GetValue<string>() ?? doc.Name; doc.Width = op["width"]?.GetValue<int>() ?? doc.Width; doc.Height = op["height"]?.GetValue<int>() ?? doc.Height; doc.Background = op["background"]?.GetValue<string>() ?? doc.Background; continue; }
            AssetWorkspace.ValidId(layerId);
            var layer = doc.Layers.SingleOrDefault(l => l.Id == layerId);
            if (action == "layer")
            {
                if (layer == null) { layer = new() { Id = layerId, Name = layerId }; doc.Layers.Add(layer); }
                layer.Name = op["name"]?.GetValue<string>() ?? layer.Name; layer.Visible = op["visible"]?.GetValue<bool>() ?? layer.Visible; layer.Opacity = op["opacity"]?.GetValue<double>() ?? layer.Opacity; continue;
            }
            if (layer == null) throw new ArgumentException("Unknown layer: " + layerId);
            if (action == "delete_layer") { doc.Layers.Remove(layer); continue; }
            if (action == "move_layer") { Move(doc.Layers, layer, op["index"]?.GetValue<int>() ?? -1); continue; }
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
        if (!Handles(name)) throw new ArgumentException("Unknown asset tool.");
        if (!await approve("assets|" + run.Chat.Id + "|" + name, "Générateur d’assets / Asset generator", args.ToJsonString(), ct)) return new("Access denied; asset unchanged.");
        await Demand();
        AssetDocument doc;
        if (name == "asset_create") doc = await workspace.CreateAsync(args["name"]?.GetValue<string>() ?? "Asset", args["width"]?.GetValue<int>() ?? 512, args["height"]?.GetValue<int>() ?? 512, args["background"]?.GetValue<string>() ?? "none", ct);
        else if (name == "asset_edit") doc = await workspace.UpdateAsync(id, args["expected_revision"]?.GetValue<int>() ?? -1, d => Edit(d, args["operations"]?.AsArray() ?? []), ct);
        else
        {
            doc = await workspace.ReadAsync(id,ct);
            if (name == "asset_capture")
            {
                int size = args["max_size"]?.GetValue<int>() ?? 1400;
                if (size is < 64 or > 2048) throw new ArgumentException("max_size: 64–2048.");
                var bytes = await Task.Run(() => AssetRenderer.Preview(doc,size),ct);
                if (bytes.Length > 8*1024*1024) throw new IOException("Capture exceeds 8 MB; lower max_size.");
                var scale=Math.Min(1,size/(float)Math.Max(doc.Width,doc.Height));
                return new(JsonSerializer.Serialize(new { asset_id=doc.Id, doc.Revision, doc.Width, doc.Height, scale, capture_width=(int)Math.Ceiling(doc.Width*scale),capture_height=(int)Math.Ceiling(doc.Height*scale),origin="top-left of artwork, no desktop coordinates" },AssetDocument.Json), new Attachment { Name=doc.Name+".png", Mime="image/png", Data=bytes },doc);
            }
            var path = await workspace.ExportAsync(doc,args["format"]?.GetValue<string>()??"svg",args["transparent"]?.GetValue<bool>()??false,args["background"]?.GetValue<string>(),args["scale"]?.GetValue<float>()??1,ct);
            return new(JsonSerializer.Serialize(new { path, asset_id=doc.Id, doc.Revision },AssetDocument.Json),Document:doc);
        }
        return new(JsonSerializer.Serialize(new { asset_id=doc.Id,doc.Revision,document=doc },AssetDocument.Json),Document:doc);
    }
}
