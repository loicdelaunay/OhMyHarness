using System.Text.Json.Nodes;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async Task<string> ExecuteBrowserScriptAsync(string script)
    {
#if WINDOWS
        return await browser.ExecuteScriptAsync(script);
#else
        return await CurrentBrowser.Chrome.EvaluateAsync(script);
#endif
    }
    async Task<string> BrowserDevToolsAsync(string method,string parameters)
    {
#if WINDOWS
        return await browser.CoreWebView2.CallDevToolsProtocolMethodAsync(method,parameters);
#else
        return (await CurrentBrowser.Chrome.CallAsync(method,JsonNode.Parse(parameters)!.AsObject())).ToJsonString();
#endif
    }
}
