using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public static class SourceTools
{
    public static bool CanRead(string skills) => new[] { "sources", "write_sources", "code_search", "patch_sources" }.Any(s => Skills.Enabled(skills, s));
    public static bool Handles(string name) => name is "glob_sources" or "grep_sources" or "patch_sources";
    public static void AddDefinitions(JsonArray definitions, bool sources, string skills)
    {
        if (!sources) return;
        void Add(string name, string description, JsonObject properties, params string[] required) => definitions.Add(new JsonObject {
            ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["description"] = description,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray(required.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), ["additionalProperties"] = false } } });
        if (Skills.Enabled(skills, "code_search"))
        {
            Add("glob_sources", "Find project files by relative glob: **/*.cs, src/**, *.md. Supports *, ** and ?. Results are bounded; excluded folders and symlinks are skipped.", new() { ["glob"] = new JsonObject { ["type"] = "string" } }, "glob");
            Add("grep_sources", "Search project text files of any extension with path:line results. Literal by default; regex=true for regex. glob defaults to **/*. Binary files and files >128 KB are skipped.", new() {
                ["query"] = new JsonObject { ["type"] = "string" }, ["glob"] = new JsonObject { ["type"] = "string" }, ["regex"] = new JsonObject { ["type"] = "boolean" }, ["ignore_case"] = new JsonObject { ["type"] = "boolean" } }, "query");
        }
        if (Skills.Enabled(skills, "patch_sources")) Add("patch_sources", "Prepare/apply multiple exact text edits with a unified diff. All edits validated first. old_text must match exactly ONCE; include context. Null old_text creates a NEW file only. Repeated paths apply sequentially. UTF-8 files <=128 KB. dry_run defaults true; false requests approval and applies after checking files are unchanged. Paths stay inside attached sources.", new() {
            ["dry_run"] = new JsonObject { ["type"] = "boolean" },
            ["edits"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 50, ["items"] = new JsonObject {
                ["type"] = "object", ["properties"] = new JsonObject {
                    ["path"] = new JsonObject { ["type"] = "string" }, ["old_text"] = new JsonObject { ["type"] = new JsonArray("string", "null") }, ["new_text"] = new JsonObject { ["type"] = "string" } },
                ["required"] = new JsonArray("path", "old_text", "new_text"), ["additionalProperties"] = false } } }, "edits");
    }
    public static async Task<string> ExecuteAsync(SourceAccess source, string name, JsonObject args, Func<string> skills, Func<string, string, CancellationToken, Task<bool>> approve, CancellationToken ct)
    {
        var required = name == "patch_sources" ? "patch_sources" : "code_search";
        void Check() { if (!Skills.Enabled(skills(), required)) throw new UnauthorizedAccessException("Skill désactivé."); }
        Check();
        if (name is "glob_sources" or "grep_sources") return await source.SearchAsync(args["glob"]?.GetValue<string>() ?? "**/*", name == "grep_sources" ? args["query"]?.GetValue<string>() ?? "" : null, args["regex"]?.GetValue<bool>() ?? false, args["ignore_case"]?.GetValue<bool>() ?? false, ct);
        if (name != "patch_sources") throw new ArgumentException("Outil inconnu.");
        var edits = (args["edits"] as JsonArray ?? throw new ArgumentException("edits requis.")).Select(e => new SourceEdit(
            e?["path"]?.GetValue<string>() ?? throw new ArgumentException("path requis."),
            e!.AsObject().ContainsKey("old_text") ? e["old_text"]?.GetValue<string>() : throw new ArgumentException("old_text requis."),
            e["new_text"]?.GetValue<string>() ?? throw new ArgumentException("new_text requis."))).ToList();
        var plan = await source.PreparePatchAsync(edits, ct);
        if (args["dry_run"]?.GetValue<bool>() != false) return "Aperçu uniquement, aucun fichier modifié.\n```diff\n" + plan.Diff + "\n```";
        var scope = "source-patch|" + string.Join("|", source.Roots);
        if (!await approve(scope, plan.Diff, ct)) return "Accès refusé. Aucun fichier modifié.";
        ct.ThrowIfCancellationRequested(); Check();
        return await source.ApplyPatchAsync(plan, ct);
    }
}
