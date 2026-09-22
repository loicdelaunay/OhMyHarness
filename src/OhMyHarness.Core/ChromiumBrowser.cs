using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using System.Text;

namespace OhMyHarness.Core;

/// <summary>An owned Chromium window/profile for each conversation. CDP is bound to loopback only.</summary>
public sealed class ChromiumBrowser : IDisposable
{
    readonly ClientWebSocket socket = new();
    readonly SemaphoreSlim send = new(1, 1);
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> calls = new();
    readonly CancellationTokenSource lifetime = new();
    Process? process;
    int sequence;
    string? mainFrameId;
    volatile string source = "about:blank";
    volatile bool disconnected;
    public string Source => source;
    public bool IsConnected => !disconnected && !lifetime.IsCancellationRequested && socket.State == WebSocketState.Open;
    public static string FindExecutable(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        string[] candidates = OperatingSystem.IsMacOS()
            ? ["/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge", "/Applications/Chromium.app/Contents/MacOS/Chromium"]
            : [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google/Chrome/Application/chrome.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft/Edge/Application/msedge.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Google/Chrome/Application/chrome.exe")];
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Installez Chrome ou Edge, ou indiquez son exécutable dans Réglages → Navigateur. / Install Chrome or Edge, or configure its executable.");
    }
    public async Task StartAsync(int chatId, string configured, CancellationToken ct, bool headless = false)
    {
        var folder = Path.Combine(PortableStorage.Root, "Browser", "chat-" + chatId);
        Directory.CreateDirectory(folder);
        var portFile = Path.Combine(folder,"DevToolsActivePort");
        if(File.Exists(portFile)) File.Delete(portFile);
        var start = new ProcessStartInfo(FindExecutable(configured)) { UseShellExecute=false };
        foreach(var arg in new[]{"--remote-debugging-port=0","--remote-debugging-address=127.0.0.1","--user-data-dir="+folder,"--no-first-run","--no-default-browser-check","--disable-background-mode","--app=about:blank"})start.ArgumentList.Add(arg);
        if (headless) start.ArgumentList.Add("--headless=new");
        process=Process.Start(start) ?? throw new IOException("Impossible de démarrer Chromium.");
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
            string[] lines=[];
            while(lines.Length<1 || !int.TryParse(lines[0],out _))
            {
                timeout.Token.ThrowIfCancellationRequested();
                if(process.HasExited)throw new IOException("Chromium s’est arrêté. Vérifiez les restrictions système.");
                if(File.Exists(portFile)) { try { lines=await File.ReadAllLinesAsync(portFile,timeout.Token); } catch(IOException) { } }
                if(lines.Length==0)await Task.Delay(100,timeout.Token);
            }
            using var client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false });
            JsonNode? page=null;
            while(page==null)
            {
                var list=JsonNode.Parse(await client.GetStringAsync($"http://127.0.0.1:{int.Parse(lines[0])}/json/list",timeout.Token))!.AsArray();
                page=list.FirstOrDefault(x=>x?["type"]?.GetValue<string>()=="page");
                if(page==null)await Task.Delay(100,timeout.Token);
            }
            var url=new Uri(page["webSocketDebuggerUrl"]!.GetValue<string>());
            if(!url.IsLoopback || url.Scheme!="ws")throw new IOException("Unexpected debug endpoint.");
            source=page["url"]?.GetValue<string>() ?? "about:blank";
            await socket.ConnectAsync(url,timeout.Token); _=ReceiveAsync();
            await CallAsync("Page.enable",new(),timeout.Token);
            await CallAsync("Browser.setDownloadBehavior",new(){["behavior"]="deny"},timeout.Token);
            await CallAsync("Browser.grantPermissions",new(){["permissions"]=new JsonArray()},timeout.Token);
        }
        catch { Dispose(); throw; }
    }
    public async Task<JsonObject> CallAsync(string method, JsonObject args, CancellationToken ct=default)
    {
        if (!IsConnected) throw new IOException("Navigateur indisponible. Relancez la navigation pour le rouvrir.");
        var id=Interlocked.Increment(ref sequence); var completion=new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously); calls[id]=completion;
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(40));
        try
        {
            var bytes=Encoding.UTF8.GetBytes(new JsonObject{["id"]=id,["method"]=method,["params"]=args.DeepClone()}.ToJsonString());
            await send.WaitAsync(timeout.Token);
            try { await socket.SendAsync(bytes,WebSocketMessageType.Text,true,timeout.Token); } finally { send.Release(); }
            return await completion.Task.WaitAsync(timeout.Token);
        }
        finally { calls.TryRemove(id,out _); }
    }
    async Task ReceiveAsync()
    {
        try
        {
            var buffer=new byte[65536];
            while(!lifetime.IsCancellationRequested)
            {
                using var message=new MemoryStream(); WebSocketReceiveResult part;
                do
                {
                    part=await socket.ReceiveAsync(buffer,lifetime.Token);
                    if(part.MessageType==WebSocketMessageType.Close)throw new IOException("Le navigateur a été fermé.");
                    message.Write(buffer,0,part.Count);
                    if(message.Length>32*1024*1024)throw new IOException("Browser response too large.");
                }while(!part.EndOfMessage);
                var json=JsonNode.Parse(message.ToArray())!.AsObject();
                if(json["method"]?.GetValue<string>() == "Page.frameNavigated" && json["params"]?["frame"] is JsonObject frame && frame["parentId"] == null)
                {
                    mainFrameId=frame["id"]?.GetValue<string>();
                    source=frame["url"]?.GetValue<string>() ?? "about:blank";
                }
                else if(json["method"]?.GetValue<string>() == "Page.navigatedWithinDocument" && json["params"]?["frameId"]?.GetValue<string>() == mainFrameId)
                    source=json["params"]?["url"]?.GetValue<string>() ?? source;
                if(json["id"] is not JsonValue value || !calls.TryRemove(value.GetValue<int>(),out var completion))continue;
                if(json["error"]!=null)completion.TrySetException(new IOException(json["error"]!.ToJsonString()));
                else completion.TrySetResult(json["result"] as JsonObject ?? new());
            }
        }
        catch(Exception ex) { disconnected=true; foreach(var pending in calls.Values)pending.TrySetException(new IOException("Navigateur indisponible : "+ex.Message,ex)); }
    }
    public async Task<string> EvaluateAsync(string expression, CancellationToken ct=default)
    {
        var result=await CallAsync("Runtime.evaluate",new(){["expression"]=expression,["returnByValue"]=true,["timeout"]=5000},ct);
        if(result["exceptionDetails"]!=null)throw new IOException(result["exceptionDetails"]!.ToJsonString());
        return result["result"]?["value"]?.ToJsonString() ?? "null";
    }
    public async Task NavigateAsync(Uri uri,CancellationToken ct)
    {
        var result=await CallAsync("Page.navigate",new(){["url"]=uri.AbsoluteUri},ct);
        if(result["errorText"] is JsonValue error)throw new IOException(error.GetValue<string>());
        for(int i=0;i<150;i++)
        {
            if(await EvaluateAsync("document.readyState",ct) is "\"complete\"" or "\"interactive\"")return;
            await Task.Delay(100,ct);
        }
    }
    public void Dispose()
    {
        if(lifetime.IsCancellationRequested)return;
        lifetime.Cancel(); socket.Dispose();
        if(process!=null) { try { if(!process.HasExited)process.Kill(true); } catch(InvalidOperationException) { } finally { process.Dispose(); } }
    }
}
