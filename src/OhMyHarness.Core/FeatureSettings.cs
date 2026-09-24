using System.Text.Json;

namespace OhMyHarness.Core;

public sealed class FeatureSettings
{
    public string Theme { get; set; } = "fluent-dark";
    public string CliTheme { get; set; } = "shared";
    public string ApplicationName { get; set; } = "OhMyHarness";
    public string LogoPath { get; set; } = "";
    public int FontZoomPercent { get; set; } = 100;
    public bool ComposerInfoExpanded { get; set; } = true;
    public bool AutoFocusTool { get; set; }
    public string BrowserMode { get; set; } = "embedded";
    public string ChromePath { get; set; } = "";
    public string RagMode { get; set; } = "local";
    public int RagProviderId { get; set; }
    public string RagModel { get; set; } = "text-embedding-3-small";
    public int RagMaxFiles { get; set; } = 500;
    public int RagTopK { get; set; } = 5;
    public int VisionProviderId { get; set; }
    public string VisionModel { get; set; } = "";
    public string VisionInstruction { get; set; } = "";
    public bool VisionComponents { get; set; }
    public bool AutoNameConversations { get; set; }
    public int NamingProviderId { get; set; }
    public string NamingModel { get; set; } = "";
    public bool LogsEnabled { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
    public int LogRetentionDays { get; set; } = 7;
    public void FilterBrowser(System.Text.Json.Nodes.JsonArray tools)
    {
        if (BrowserMode == "embedded") return;
        for (int i=tools.Count-1; i>=0; i--)
        {
            var name=tools[i]?["function"]?["name"]?.GetValue<string>() ?? "";
            if(name.StartsWith("browser_") || name is "browse" or "read_page" or "inspect_dom" or "open_local_file") tools.RemoveAt(i);
        }
    }
    public static FeatureSettings Read(string json) { try { return JsonSerializer.Deserialize<FeatureSettings>(json) ?? new(); } catch { return new(); } }
    public string Json()
    {
        if (VisionInstruction.Length > 8000 || NamingModel.Length > 200 || NamingProviderId < 0 || LogRetentionDays is < 1 or > 365 || !Enum.TryParse<AppLogLevel>(LogLevel, out _))
            throw new ArgumentException("Réglages vision, nommage ou logs invalides.");
        if (BrowserMode is not ("embedded" or "chrome" or "disabled") || RagMode is not ("local" or "api") || RagMaxFiles is < 1 or > 2000 || RagTopK is < 1 or > 20 || RagModel.Length > 200 || VisionProviderId < 0 || VisionModel.Length > 200)
            throw new ArgumentException("Réglages navigateur/RAG invalides.");
        return JsonSerializer.Serialize(this);
    }
    public McpServer ChromeServer(int chatId) => new()
    {
        Id = int.MaxValue, Name = "Chrome DevTools", Enabled = BrowserMode == "chrome", Command = OperatingSystem.IsWindows() ? "npx.cmd" : "npx",
        ArgumentsJson = JsonSerializer.Serialize(new[] { "-y", "chrome-devtools-mcp@latest", "--no-usage-statistics", "--user-data-dir=" + Path.Combine(PortableStorage.Root, "Chrome", "chat-" + chatId) }
            .Concat(string.IsNullOrWhiteSpace(ChromePath) ? [] : new[] { "--executable-path=" + ChromePath }))
    };
}

public sealed class RagChunk
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Model { get; set; } = "";
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Text { get; set; } = "";
    public byte[] Vector { get; set; } = [];
}
