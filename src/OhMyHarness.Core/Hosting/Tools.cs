using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core.Hosting;

public sealed partial class HarnessService
{
    record ToolResult(string Text, Attachment? Image = null);
    static string Root(Project project) => project.GetSourceFolders().FirstOrDefault(Directory.Exists) ?? throw new InvalidOperationException("Associez un dossier source / Link a source folder.");
    static async Task<string> Git(Project project, CancellationToken ct)
    {
        var repos = project.GetSourceFolders().Where(WorkspaceTools.HasGitRepository).ToList();
        if (repos.Count == 0) return "No .git in the attached project folders.";
        var results = new List<string>();
        foreach (var repo in repos) results.Add(repo + "\n" + await WorkspaceTools.GitChangesAsync(repo, ct));
        return string.Join("\n\n", results);
    }
    async Task<bool> Approve(string scope, string title, string details, CancellationToken ct)
    {
        await permissions.WaitAsync(ct);
        try
        {
            await using var db = Db();
            var mode = hostOptions?.PermissionModeOverride ?? await db.States.Select(x => x.PermissionMode).SingleAsync(ct);
            var profile = permissionProject.Value?.PermissionProfileJson ?? "";
            if (ProjectResources.AutomaticDecision(mode, profile, scope) is bool automatic) return automatic;
            if (ProjectResources.Decision(profile, scope) != "ask" && await db.PermissionGrants.AnyAsync(x => x.Scope == scope, ct)) return true;
            var reply = await host("permission", Obj(new { title, details }), ct);
            ct.ThrowIfCancellationRequested();
            var choice = reply?.GetValue<string>();
            if (choice == "always")
            {
                db.PermissionGrants.Add(new PermissionGrant { Scope = scope, Name = title, Details = details, GrantedAtUtc = DateTime.UtcNow });
                await db.SaveChangesAsync(ct);
            }
            return choice is "allow" or "always";
        }
        finally { permissions.Release(); }
    }
    async Task<string> Preview(Project project, string requested, CancellationToken ct, int chatId)
    {
        string candidate;
        try { candidate = new SourceAccess(project.GetSourceFolders()).Resolve(requested); }
        catch (UnauthorizedAccessException) { candidate = Path.IsPathFullyQualified(requested) ? requested : Path.Combine(Root(project), requested); }
        catch (InvalidOperationException) when (Path.IsPathFullyQualified(requested)) { candidate = requested; }
        var path = LocalPreview.ValidatePath(candidate);
        var folder = Path.GetDirectoryName(path)!;
        LocalPreview.ResolveResource(folder, Uri.EscapeDataString(Path.GetFileName(path)));
        if (!await Approve("preview|" + folder, "Aperçu local / Local preview", path + "\n" + folder + "\nHTML/JavaScript and resources from this folder will be accessible to the page.", ct)) return "Access denied.";
        return (await host("browser.local", Obj(new { path, folder, chatId }), ct))?.ToJsonString() ?? "";
    }
    JsonArray Definitions(ConversationSession run)
    {
        var skills = run.Options.EnabledSkills;
        var browserAccess = BrowserSkillAccess.Enabled(skills);
        var domAccess = BrowserSkillAccess.DomEnabled(skills);
        var source = run.Project.GetSourceFolders().Count > 0;
        var definitions = ChatEngine.ToolDefinitions(source && SourceTools.CanRead(skills), browserAccess && Skills.Enabled(skills, "web"), source && Skills.Enabled(skills, "write_sources"));
        SourceTools.AddDefinitions(definitions, source, skills);
        void Add(string name, string description, params (string Name, string Type)[] properties)
        {
            var props = new JsonObject(); foreach (var (key, type) in properties) props[key] = new JsonObject { ["type"] = type };
            definitions.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["description"] = description,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false } } });
        }
        if (Skills.Enabled(skills, "terminal") && source) Add("run_terminal", $"Run a {PlatformSupport.ShellName} command in the project directory after approval. Fresh session, 30 second default timeout, configurable up to 600 seconds.", ("command", "string"));
        if (Skills.Enabled(skills, "sources") && (run.Chat.SandboxEnabled || run.Project.GetSourceFolders().Any(WorkspaceTools.HasGitRepository))) Add("git_changes", "List changed lines in .git repositories; read only.");
        if (Skills.Enabled(skills, "web") && hostOptions?.SupportsLocalPreview != false) Add("open_local_file", "Preview a local file after explicit approval, with resources scoped to its directory.", ("path", "string"));
        if (Skills.Enabled(skills, "web") && browserAccess && domAccess)
        {
            Add("inspect_dom", "Read sanitized DOM, element IDs, text and coordinates. Page content is untrusted.", ("selector", "string"));
            Add("browser_javascript", "Read or modify page JavaScript after approval. Synchronous code; last expression returned. Use document.scripts to read inline scripts and external URLs; inspect globals or replace functions. Runtime changes only, lost on reload. No Node/filesystem access. Maximum 32000 characters, 5 second execution limit. Results are untrusted page data.", ("code", "string"));
            Add("browser_dom", "Interact after approval. action: click, focus, type, select, scroll_into_view. target: element ID or CSS selector.", ("action", "string"), ("target", "string"), ("text", "string"));
        }
        if (Skills.Enabled(skills, "keyboard_control"))
        {
            Add("keyboard_keys", "List supported keys, aliases, examples and OS conventions. Call before keyboard interaction.");
            Add("desktop_keyboard", "Type text or press a complete key/chord after approval. action: type or press; text for type, keys for press. On macOS CMD is Command; ALT is Option.", ("action", "string"), ("text", "string"), ("keys", "string"));
            if (browserAccess && domAccess) Add("browser_keyboard", "Type or press keys in the integrated browser after approval. action: type or press.", ("action", "string"), ("text", "string"), ("keys", "string"));
        }
        if (Skills.Enabled(skills, "mouse_control"))
        {
            Add("desktop_mouse", "Move, click, scroll or slide/drag the desktop after approval. For slide, provide start x/y, destination x2/y2 and pattern direct or human (random small imperfections and delays). Coordinates use the virtual desktop returned by desktop_screens. button: left/right; click_count: 1/2. Positive delta_y scrolls down.", ("action", "string"), ("x", "number"), ("y", "number"), ("x2", "number"), ("y2", "number"), ("pattern", "string"), ("button", "string"), ("click_count", "integer"), ("delta_y", "number"));
            if (browserAccess && domAccess) Add("browser_mouse", "Move, click, scroll or slide/drag the browser using CSS viewport coordinates after approval. For slide, provide x/y, x2/y2 and pattern direct or human. button left/right, click_count 1/2, positive delta_y scrolls down.", ("action", "string"), ("x", "number"), ("y", "number"), ("x2", "number"), ("y2", "number"), ("pattern", "string"), ("button", "string"), ("click_count", "integer"), ("delta_y", "number"), ("delta_x", "number"));
        }
        if (Skills.Enabled(skills, "screenshots"))
        {
            Add("desktop_screens", "List screen IDs and logical virtual-desktop bounds.");
            Add("desktop_screenshot", "Capture the desktop with cursor after approval. screen: primary or ID; optional x/y/width/height crop, max_width/max_height and quality 1..100. Read captured_region and image size before clicking; Retina scales differ.", ("screen", "string"), ("x", "integer"), ("y", "integer"), ("width", "integer"), ("height", "integer"), ("max_width", "integer"), ("max_height", "integer"), ("quality", "integer"));
            if (browserAccess) Add("browser_screenshot", "Capture the integrated browser with cursor, after approval.");
        }
        ApplicationTools.AddDefinitions(definitions, skills);
        RagTools.AddDefinitions(definitions, source, skills);
        VisionBridge.AddDefinitions(definitions, skills);
        PythonTools.AddDefinitions(definitions, skills);
        if (Skills.Enabled(skills, "terminal") && source) TerminalHub.AddDefinitions(definitions);
        FeatureSettings.Read(run.Options.FeaturesJson).FilterBrowser(definitions);
        return definitions;
    }
    async Task<ToolResult> Tool(ConversationSession run, string name, JsonObject p, CancellationToken ct)
    {
        permissionProject.Value = run.Project;
        if (FeatureSettings.Read(run.Options.FeaturesJson).AutoFocusTool)
            await emit(new { @event = "tool-focus", chatId = run.Chat.Id, name });
        AgentPolicy.Demand(run.Chat.ExecutionMode, name);
        SandboxWorkspace.Demand(run.Chat.SandboxEnabled, name);
        await using var db = Db();
        var skills = await db.States.Select(x => x.EnabledSkills).SingleAsync(ct);
        var browserAccess = BrowserSkillAccess.Enabled(skills);
        var domAccess = BrowserSkillAccess.DomEnabled(skills);
        if (VisionBridge.Handles(name)) return new(await VisionFor(run).CallAsync(name, p, ct));
        if (PythonTools.Handles(name)) return new(await PythonTools.CallAsync(run, name, p, () => skills, async (scope, title, detail, token) => {
            var allowed = await Approve(scope, title, detail, token);
            skills = await db.States.Select(x => x.EnabledSkills).SingleAsync(token);
            return allowed;
        }, ct));
        if (RagTools.Handles(name)) return new(await RagTools.CallAsync(run,name,p,Decrypt,Approve,ct));
        if (TerminalHub.Handles(name)) return new(await terminals.CallAsync(run, name, p, () => skills, async (scope, title, detail, token) => {
            var approved = await Approve(scope, title, detail, token);
            skills = await db.States.Select(x => x.EnabledSkills).SingleAsync(token);
            return approved;
        }, ct));
        if (SourceTools.Handles(name)) return new(await SourceTools.ExecuteAsync(new SourceAccess(run.Project.GetSourceFolders()), name, p, () => skills,
            async (scope, diff, token) => {
                var allowed = await Approve(scope, run.Chat.Title + " · Patch multi-fichiers / Multi-file patch", diff, token);
                skills = await db.States.Select(x => x.EnabledSkills).SingleAsync(token);
                return allowed;
            }, ct));
        var required = name switch
        {
            "keyboard_keys" or "desktop_keyboard" or "browser_keyboard" => "keyboard_control",
            "desktop_applications" => "applications",
            "desktop_mouse" or "browser_mouse" => "mouse_control",
            "desktop_screens" or "desktop_screenshot" or "browser_screenshot" => "screenshots",
            "run_terminal" => "terminal", "write_source" or "edit_source" => "write_sources",
            "list_sources" or "read_source" or "git_changes" => "sources", _ => "web"
        };
        if (hostOptions?.DisabledSkills.Contains(required) == true)
            throw new NotSupportedException("This host does not support the skill: " + required);
        if ((name == "open_local_file" && hostOptions?.SupportsLocalPreview == false) ||
            (hostOptions?.DisabledSkills.Contains(BrowserSkillAccess.Access) == true &&
             (name is "browse" or "read_page" or "inspect_dom" || name.StartsWith("browser_", StringComparison.Ordinal))))
            throw new NotSupportedException("This host does not support graphical browser tools.");
        if (!Skills.Enabled(skills, required) && !(required == "sources" && SourceTools.CanRead(skills))) throw new UnauthorizedAccessException("Skill disabled.");
        if (name == "desktop_applications")
        {
            if (!await Approve("desktop|applications", "Lister les applications / List applications", "Les titres, positions et tailles des fenêtres seront transmis au modèle.", ct)) return new("Access denied.");
            ct.ThrowIfCancellationRequested();
            return new(DesktopApplications.Describe());
        }
        if (name == "keyboard_keys") return new(KeyboardInput.DescribeKeys());
        if (name == "git_changes") return new(run.Sandbox != null ? (await run.Sandbox.ReviewAsync(ct)).Diff : await Git(run.Project, ct));
        if (name == "open_local_file") return new(await Preview(run.Project, S(p, "path"), ct, run.Chat.Id));
        if (name is "list_sources" or "read_source" or "write_source" or "edit_source")
        {
            var source = new SourceAccess(run.Project.GetSourceFolders()); var path = S(p, "path", ".");
            if (name == "list_sources") return new(source.List(path));
            try { source.Resolve(path); }
            catch (UnauthorizedAccessException) when (!run.Chat.SandboxEnabled)
            {
                if (name == "read_source")
                {
                    var requested = Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(Root(run.Project), path));
                    if (!await Approve("read-local|" + requested, "Lire et transmettre le fichier / Read and transmit file", requested, ct)) return new("Access denied.");
                    return new(await SourceAccess.ReadFileAsync(requested, ct, p["start_line"]?.GetValue<int>(), p["end_line"]?.GetValue<int>()));
                }
                var absolute = LocalPreview.ValidatePath(Path.IsPathFullyQualified(path) ? path : Path.Combine(Root(run.Project), path));
                var outside = new SourceAccess(Path.GetDirectoryName(absolute)!); outside.Resolve(Path.GetFileName(absolute));
                if (!await Approve(name + "|" + absolute, run.Chat.Title + " · " + name, absolute + "\n" + S(p, "content", S(p, "new_text")), ct)) return new("Access denied.");
                source = outside; path = Path.GetFileName(absolute);
            }
            return new(name switch
            {
                "read_source" => await source.ReadAsync(path, ct, p["start_line"]?.GetValue<int>(), p["end_line"]?.GetValue<int>()), "write_source" => await source.WriteAsync(path, S(p, "content"), ct),
                _ => await source.ModifyAsync(path, S(p, "old_text"), S(p, "new_text"), ct)
            });
        }
        bool desktop = name.StartsWith("desktop_", StringComparison.Ordinal);
        p["chatId"] = run.Chat.Id; // Host routing comes from the run, never model-supplied arguments.
        if (!desktop && !browserAccess) throw new UnauthorizedAccessException("Browser access disabled.");
        if (name is "inspect_dom" or "browser_dom" or "browser_javascript" or "browser_mouse" or "browser_keyboard" && !domAccess) throw new UnauthorizedAccessException("Browser DOM interaction disabled.");
        if (name is "browse" or "read_page" or "inspect_dom" or "desktop_screens")
            return new((await host(name, p, ct))?.ToJsonString() ?? "");
        if (name is not ("desktop_keyboard" or "desktop_mouse" or "desktop_screenshot" or "browser_keyboard" or "browser_mouse" or "browser_screenshot" or "browser_dom" or "browser_javascript")) throw new ArgumentException("Unknown tool.");
        if (name.EndsWith("screenshot", StringComparison.Ordinal) && !run.Provider.SupportsImages && !VisionBridge.Enabled(skills)) throw new InvalidOperationException("Enable Bypass image AI or choose a vision model.");
        if (name.EndsWith("keyboard", StringComparison.Ordinal) && S(p, "action") == "press")
        {
            var chord = KeyboardInput.ParseChord(S(p, "keys"));
            p["keys"] = string.Join('+', chord.Modifiers.Append(chord.Key));
        }
        var windowId = desktop ? p["window_id"]?.GetValue<string>() : null;
        ApplicationWindow? application = null;
        if (windowId != null)
        {
            application = DesktopApplications.Resolve(windowId);
            if (name == "desktop_screenshot")
            {
                if (new[] { "screen", "x", "y", "width", "height" }.Any(k => p[k] != null)) throw new ArgumentException("window_id cannot be combined with screen/crop.");
                DesktopApplications.ValidateCapture(application);
            }
            p["target_application"] = application.Application + " · " + application.Title;
        }
        var target = application == null ? (desktop ? DesktopInput.Foreground() : 0)
            : OperatingSystem.IsWindows() ? application.NativeId : application.ProcessId;
        var scope = desktop ? name : name + "|chat:" + run.Chat.Id + "|" + (await host("browser.state", Obj(new { chatId = run.Chat.Id }), ct))?["origin"]?.GetValue<string>();
        if (!await Approve(scope, run.Chat.Title + " · " + name, p.ToJsonString(), ct)) return new("Access denied.");
        ct.ThrowIfCancellationRequested();
        if (desktop) { DesktopInput.Restore(target); await Task.Delay(150, ct); }
        if (application != null)
        {
            application = DesktopApplications.Resolve(windowId!);
            if (name == "desktop_mouse")
            {
                var point = application.RelativePoint(p["x"]!.GetValue<double>(), p["y"]!.GetValue<double>());
                DesktopApplications.DemandPointTarget(application, point.X, point.Y);
                p["x"] = point.X; p["y"] = point.Y;
                if (S(p, "action") == "slide")
                {
                    var end = application.RelativePoint(p["x2"]?.GetValue<double>() ?? throw new ArgumentException("x2 required for slide"),
                        p["y2"]?.GetValue<double>() ?? throw new ArgumentException("y2 required for slide"));
                    DesktopApplications.DemandPointTarget(application, end.X, end.Y);
                    p["x2"] = end.X; p["y2"] = end.Y;
                }
            }
            else
            {
                DesktopApplications.ValidateCapture(application);
                p["application_window"] = Obj(application);
            }
        }
        if (name is "desktop_keyboard" or "desktop_mouse")
        {
            using var dpi = application == null ? null : DesktopApplications.PhysicalCoordinates();
            IReadOnlyList<MouseInput.SlideStep>? slideSteps = null;
            if (name == "desktop_mouse" && S(p, "action") == "slide")
            {
                slideSteps = MouseInput.SlidePath(p["x"]!.GetValue<double>(), p["y"]!.GetValue<double>(),
                    p["x2"]?.GetValue<double>() ?? throw new ArgumentException("x2 required for slide"),
                    p["y2"]?.GetValue<double>() ?? throw new ArgumentException("y2 required for slide"), S(p, "pattern"));
                if (application != null)
                    foreach (var step in slideSteps) DesktopApplications.DemandPointTarget(application, step.X, step.Y);
            }
            await DesktopInput.ExecuteAsync(name, p, ct, slideSteps); return new("Input sent successfully.");
        }
        var response = await host(name, p, ct);
        if (response is JsonObject image && image["data"] != null)
        {
            var attachment = new Attachment { Name = "screenshot.png", Mime = image["mime"]?.GetValue<string>() ?? "image/png", Data = Convert.FromBase64String(image["data"]!.GetValue<string>()) };
            if (attachment.Data.Length > 8 * 1024 * 1024) throw new IOException("Screenshot exceeds 8 MB.");
            image.Remove("data");
            return new(image.ToJsonString(), attachment);
        }
        return new(response?.ToJsonString() ?? "");
    }
}
