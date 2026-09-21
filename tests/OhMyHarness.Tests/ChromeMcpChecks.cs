using OhMyHarness.Core;
using ModelContextProtocol.Client;
using System.Text.Json;

static class ChromeMcpChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var server=new FeatureSettings {BrowserMode="chrome"}.ChromeServer(987654);
        var args=JsonSerializer.Deserialize<List<string>>(server.ArgumentsJson)!;args.Add("--headless");server.ArgumentsJson=JsonSerializer.Serialize(args);
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client=await McpSession.ConnectAsync(server,new McpSecrets(),timeout.Token);
        var tools=await client.ListToolsAsync(cancellationToken:timeout.Token);
        check(tools.Any(x=>x.Name=="evaluate_script") && tools.Any(x=>x.Name=="navigate_page"),"Chrome DevTools MCP expose navigation et JavaScript");
        var pages=JsonSerializer.SerializeToNode(await client.CallToolAsync("list_pages", new Dictionary<string,object?>(), cancellationToken:timeout.Token));
        var listing=pages!["content"]![0]!["text"]!.GetValue<string>();
        var pageId=int.Parse(System.Text.RegularExpressions.Regex.Match(listing,@"(?m)^(\d+):").Groups[1].Value);
        await client.CallToolAsync("navigate_page",new Dictionary<string,object?> { ["pageId"]=pageId, ["url"]="about:blank" },cancellationToken:timeout.Token);
        var result=await client.CallToolAsync("evaluate_script",new Dictionary<string,object?> { ["pageId"]=pageId, ["function"]="() => ({answer: 6 * 7, title: document.title})" },cancellationToken:timeout.Token);
        check(JsonSerializer.Serialize(result).Contains("42"),"Chrome réel piloté par MCP dans un profil de test indépendant");
    }
}
