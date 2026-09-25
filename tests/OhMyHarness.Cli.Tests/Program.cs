using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using OhMyHarness.Cli;
using OhMyHarness.Core;
using OhMyHarness.Core.Hosting;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("OK: " + name); passed++; }
Check(Skills.All.Where(s => !CliClient.DesktopSkills.Contains(s.Id)).Any(s => s.Id == CompleteDesignSkill.Id),
    "Complete design remains available in the CLI skill picker");
Check(CliClient.DesktopSkills.Contains(BrowserSkillAccess.Access) && CliClient.DesktopSkills.Contains(BrowserSkillAccess.Dom),
    "Embedded browser access skills stay hidden from the CLI picker");
Check(!CliClient.DesktopSkills.Contains("web"), "Direct HTTP web research available in CLI skill picker");
await CliThemeChecks.Run(Check);
await ConnectChecks.Run(Check);
CompletionChecks.Run(Check);
InputChecks.Run(Check);
var clean = TerminalText.Clean("\x1b]52;c;secret\aHello\x1b[2J\u202e");
Check(!clean.Contains('\x1b') && !clean.Contains('\a') && !clean.Contains('\u202e'), "Untrusted text cannot issue terminal/clipboard/bidi control sequences");
Check(TerminalText.Fit("é界x", 3) == "é界", "French and wide glyphs respect terminal cell width");
var input = new InputBuffer(); input.Insert("first\nsecond"); input.Key(new('\0', ConsoleKey.LeftArrow, false, false, false)); input.Insert("!");
Check(input.Text == "first\nsecon!d", "Multiline editing retains newlines and insertion position");
input.Set("a😀"); input.Key(new('\b', ConsoleKey.Backspace, false, false, false));
Check(input.Text == "a", "Backspace removes an entire Unicode grapheme");
Check(TerminalKeys.Character('\r').Key == ConsoleKey.Enter && TerminalKeys.Character('\n').Key == ConsoleKey.J && TerminalKeys.Character('\n').Modifiers == ConsoleModifiers.Control,
    "VT input distinguishes Send from the newline shortcut");
Check(TerminalKeys.Sequence("\x1b[D")?.Key == ConsoleKey.LeftArrow && TerminalKeys.Sequence("\x1b[6~")?.Key == ConsoleKey.PageDown,
    "VT navigation sequences preserve composer and history navigation");
var paste = new TerminalPaste(); bool finished = false;
foreach (char c in "one\r\ntwo\nthree\x1b[201") finished |= paste.Feed(c);
Check(!finished && paste.Feed('~') && paste.Take() == "one\ntwo\nthree", "Fragmented bracketed paste retains lines until the complete delimiter");
foreach (char c in new string('x', 128_010) + "\x1b[201~") finished = paste.Feed(c);
Check(finished && paste.TooLong && paste.Take() == "", "Oversized paste drains its delimiter without sending content");
try { CliOptions.Parse(["run", "--unknown"]); throw new Exception("Unknown option accepted"); } catch (ArgumentException) { Check(true, "Unknown flags fail before initialization"); }
Check(CliOptions.Parse(["run", "review"]).Directory == Environment.CurrentDirectory, "CLI attaches current project directory by default");
Check(TerminalUi.DemoFrame().Contains("Démonstration hors ligne"), "Preview explicitly identifies synthetic demo content");
foreach (var theme in AppearanceThemes.All)
{
    var palette = Palette.FromTheme(theme.Id);
    Check(palette.Background == Convert.ToInt32(theme.Background[1..], 16) && palette.Foreground == Convert.ToInt32(theme.Text[1..], 16) &&
        ThemeContrast.Ratio($"#{palette.Accent:X6}", $"#{palette.Panel:X6}") >= 4.5,
        "CLI preserves theme surfaces and readable accents: " + theme.Id);
}

string root = Path.Combine(Path.GetTempPath(), "omh-cli-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string database = Path.Combine(root, "database.sqlite"), sources = Path.Combine(root, "project");
Directory.CreateDirectory(sources);
var previous = PortableStorage.Root;
var originalOut = Console.Out; var originalError = Console.Error;
try
{
    await using var api = new FakeApi();
    await using (var db = new HarnessDb(database))
    {
        await db.InitializeAsync();
        var provider = await db.Providers.FirstAsync(); provider.BaseUrl = api.Url; provider.Model = "test";
        var state = await db.States.SingleAsync(); state.ProviderId = provider.Id;
        state.EnabledSkills = "sources,write_sources,terminal,web,mouse_control,screenshots";
        state.PermissionMode = "allow"; await db.SaveChangesAsync();
    }
    async Task<(int Code, string Output, string Errors)> Run(bool execute = false, bool json = false, bool allow = false)
    {
        using var output = new StringWriter(); using var errors = new StringWriter();
        Console.SetOut(output); Console.SetError(errors);
        try
        {
            int code = await Headless.RunAsync(new() { Run = true, Execute = execute, Allow=allow, Json = json, Database = database, Directory = sources, Prompt = "Test the CLI" }).WaitAsync(TimeSpan.FromSeconds(20));
            return (code, output.ToString(), errors.ToString());
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
    }
    api.Respond = (_, _) => FakeApi.Text("Bonjour CLI");
    var plain = await Run();
    Check(plain.Code == 0 && plain.Output.Trim() == "Bonjour CLI", "Headless completion streams once and returns success");
    await using (var db = new HarnessDb(database))
    {
        Check(await db.Messages.AnyAsync(m => m.Content == "Bonjour CLI" && m.State == "complete"), "Headless history is persisted in portable SQLite");
        Check((await db.States.SingleAsync()).EnabledSkills.Contains("web"), "CLI capability filtering does not rewrite shared desktop settings");
    }
    var firstRequest = api.Requests.First();
    var names = firstRequest["tools"]!.AsArray().Select(t => t!["function"]!["name"]!.GetValue<string>()).ToList();
    Check(!names.Any(n => n.StartsWith("desktop_") || n is "browse" or "open_local_file"), "Unsupported host tools are not advertised to the model");
    int step = 0; JsonObject? resumed = null;
    api.Respond = (body, _) => Interlocked.Increment(ref step) == 1
        ? FakeApi.Tool("write_source", new { path = "blocked.txt", content = "must not write" })
        : (resumed = body) != null ? FakeApi.Text("Plan preserved") : "";
    var plan = await Run();
    Check(plan.Code == 0 && !File.Exists(Path.Combine(sources, "blocked.txt")) && resumed!.ToJsonString().Contains("plan", StringComparison.OrdinalIgnoreCase), "Headless defaults to Plan and blocks a model attempting to write");
    step = 0; resumed = null;
    api.Respond = (body, _) => Interlocked.Increment(ref step) == 1
        ? FakeApi.Tool("run_terminal", new { command = "echo unsafe > sentinel.txt" })
        : (resumed = body) != null ? FakeApi.Text("Permission preserved") : "";
    var denied = await Run(execute: true);
    Check(denied.Code == 0 && !File.Exists(Path.Combine(sources, "sentinel.txt")) && resumed!.ToJsonString().Contains("denied", StringComparison.OrdinalIgnoreCase),
        "Non-interactive sensitive tools denied despite global Allow setting");
    step = 0; resumed = null;
    api.Respond = (body, _) => Interlocked.Increment(ref step) == 1
        ? FakeApi.Tool("web_http_request", new { url = "https://example.com/" })
        : (resumed = body) != null ? FakeApi.Text("HTTP permission preserved") : "";
    var deniedHttp = await Run(execute: true);
    Check(deniedHttp.Code == 0 && resumed!.ToJsonString().Contains("permission denied", StringComparison.OrdinalIgnoreCase), "CLI dispatches HTTP and honors headless permission denial");
    var httpNames = resumed!["tools"]!.AsArray().Select(x => x!["function"]!["name"]!.GetValue<string>()).ToList();
    Check(httpNames.Contains("web_http_request") && httpNames.Contains("web_http_configure") && !httpNames.Contains("open_local_file"), "CLI advertises HTTP without graphical preview");
    step = 0; resumed = null;
    api.Respond = (body, _) => Interlocked.Increment(ref step) == 1
        ? FakeApi.Tool("open_local_file", new { path = "index.html" })
        : (resumed = body) != null ? FakeApi.Text("Preview rejected") : "";
    var preview = await Run(execute: true);
    Check(preview.Code == 0 && resumed!.ToJsonString().Contains("does not support"), "CLI HTTP does not enable graphical file previews");
    step = 0; resumed = null;
    api.Respond = (body, _) => Interlocked.Increment(ref step) == 1
        ? FakeApi.Tool("desktop_mouse", new { action = "move", x = 0, y = 0 })
        : (resumed = body) != null ? FakeApi.Text("Unsupported host tool rejected") : "";
    var unsupported = await Run(execute: true);
    Check(unsupported.Code == 0 && resumed!.ToJsonString().Contains("does not support", StringComparison.Ordinal),
        "Model-supplied graphical tool calls are rejected at dispatch even when desktop skills were enabled");
    api.Respond = (_, _) => FakeApi.Text("Structured answer");
    var json = await Run(json: true);
    var events = json.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).ToList();
    Check(events.Any(e => e["event"]!.GetValue<string>() == "stream") && events.Last()["event"]!.GetValue<string>() == "done" && !json.Output.Contains('\x1b'), "JSON automation output is valid NDJSON without ANSI rendering");
    Check(events.Last()["durationSeconds"]!.GetValue<double>() >= 0, "JSON completion reports elapsed duration");
    await using(var assetDb=new HarnessDb(database))
    {var settings=await assetDb.States.SingleAsync();settings.EnabledSkills+=","+AssetTools.SkillId;foreach(var p in await assetDb.Providers.ToListAsync())p.SupportsImages=true;await assetDb.SaveChangesAsync();}
    step=0;resumed=null;string assetId="";
    api.Respond=(body,_)=>
    {
        int round=Interlocked.Increment(ref step);
        if(round==1)return FakeApi.Tool("asset_create",new{name="CLI asset",width=64,height=64});
        if(round==2)
        {assetId=JsonNode.Parse(body["messages"]!.AsArray().Last(m=>m?["role"]?.GetValue<string>()=="tool")!["content"]!.GetValue<string>())!["asset_id"]!.GetValue<string>();return FakeApi.Tool("asset_edit",new{asset_id=assetId,expected_revision=1,operations=new[]{new{action="shape",layer_id="layer-1",shape=new{id="red",type="rect",fill="#FF0000",width=64,height=64}}}});}
        if(round==3)return FakeApi.Tool("asset_capture",new{asset_id=assetId});
        if(round==4){resumed=body;return FakeApi.Tool("asset_export",new{asset_id=assetId,format="png",transparent=true});}
        return FakeApi.Text("Asset complete");
    };
    var assetRun=await Run(execute:true,allow:true);
    Check(assetRun.Code==0 && assetRun.Output.Contains("Asset complete"),"CLI runs complete asset create/edit/capture/export sequence");
    Check(resumed!.ToJsonString().Contains("data:image/png;base64,"),"Asset capture reaches the vision model through CLI history");
    Check(Directory.EnumerateFiles(Path.Combine(root,"assets"),"*.png",SearchOption.AllDirectories).Any(),"CLI exports asset PNG beside the portable database");
    api.Respond = (_, _) => FakeApi.Tool("question", new { questions = new[] { new { question = "Choose an option", options = new[] { new { label = "A", description = "First" }, new { label = "B", description = "Second" } }, custom = false } } });
    var question = await Run(json: true);
    Check(question.Code == 3 && question.Output.Contains("\"event\":\"question\""), "Headless questions return an explicit needs-input code instead of hanging");
    await using (var db = new HarnessDb(database))
    {
        var state = await db.States.SingleAsync();
        state.FeaturesJson = new FeatureSettings { AutoNameConversations = true, NamingProviderId = state.ProviderId, NamingModel = "naming-small" }.Json();
        await db.SaveChangesAsync();
    }
    api.Respond = (body, _) => FakeApi.Text(body["model"]?.GetValue<string>() == "naming-small" ? "Titre du CLI" : "Réponse principale");
    var named = await Run();
    int namedChatId;
    await using (var db = new HarnessDb(database))
    {
        var chat = await db.Chats.OrderByDescending(c => c.Id).FirstAsync(); namedChatId = chat.Id;
        Check(named.Code == 0 && chat.Title == "Titre du CLI", "CLI automatically names new chats using the separately configured model");
    }
    await using (var cli = new CliClient(new() { Database = database }, (_, _, _) => Task.FromResult<JsonNode?>(null), _ => Task.CompletedTask))
    {
        await cli.Call("chat.favorite", new { id = namedChatId, favorite = true });
        Check((await cli.Snapshot()).Chats.First().Id == namedChatId && (await cli.Snapshot()).Chats.First().IsFavorite, "CLI favorites persist and sort before ordinary conversations");
        await cli.State(state => { var settings = FeatureSettings.Read(state.FeaturesJson); settings.AutoNameConversations = false; state.FeaturesJson = settings.Json(); });
        Check((await cli.Call("chat.autoname", new { id = namedChatId }))?.GetValue<string>() == "Titre du CLI", "Manual CLI naming remains available with automatic naming disabled");
    }
    Check(api.Error == null, "Fake provider processed real HTTP requests without harness errors");
}
finally
{
    Console.SetOut(originalOut); Console.SetError(originalError);
    await AppLog.FlushAsync();
    AppLog.Configure(new FeatureSettings { LogsEnabled = false });
    PortableStorage.UseDatabase(Path.Combine(previous, "database.sqlite"));
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    Directory.Delete(root, true);
}
Console.WriteLine($"{passed} CLI checks passed.");

sealed class FakeApi : IAsyncDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource cancellation = new();
    readonly Task server;
    public ConcurrentQueue<JsonObject> Requests = new();
    public Func<JsonObject, int, string> Respond = (_, _) => Text("test");
    public Exception? Error;
    public string Url { get; }
    public FakeApi() { listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/v1"; server = Serve(); }
    async Task Serve()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellation.Token); await using var stream = client.GetStream();
                var header = new StringBuilder(); var one = new byte[1];
                while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (header.Length > 64000 || await stream.ReadAsync(one, cancellation.Token) == 0) throw new IOException("Invalid HTTP header.");
                    header.Append((char)one[0]);
                }
                int size = int.Parse(header.ToString().Split("\r\n").First(x => x.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1]);
                var bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, cancellation.Token);
                var body = JsonNode.Parse(bytes)!.AsObject(); Requests.Enqueue(body);
                var payload = Encoding.UTF8.GetBytes(Respond(body, Requests.Count));
                var response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, cancellation.Token); await stream.WriteAsync(payload, cancellation.Token);
            }
        }
        catch (Exception ex) when (cancellation.IsCancellationRequested) { _ = ex; }
        catch (Exception ex) { Error = ex; }
    }
    static string Event(object value) => "data: " + System.Text.Json.JsonSerializer.Serialize(value) + "\n\n";
    public static string Text(string text) => Event(new { choices = new[] { new { delta = new { content = text } } } })
        + Event(new { choices = new[] { new { delta = new { }, finish_reason = "stop" } }, usage = new { prompt_tokens = 32, completion_tokens = 4 } }) + "data: [DONE]\n\n";
    public static string Tool(string name, object args) => Event(new { choices = new[] { new { delta = new { tool_calls = new[] { new { index = 0, id = "call_" + name, type = "function", function = new { name, arguments = System.Text.Json.JsonSerializer.Serialize(args) } } } } } } })
        + Event(new { choices = new[] { new { delta = new { }, finish_reason = "tool_calls" } } }) + "data: [DONE]\n\n";
    public async ValueTask DisposeAsync() { cancellation.Cancel(); listener.Stop(); await server; cancellation.Dispose(); }
}
