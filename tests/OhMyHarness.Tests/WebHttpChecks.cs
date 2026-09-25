using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using OhMyHarness.Core;

static class WebHttpChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        using var server = new Server();
        using var second = new Server();
        using var web = new WebHttpTools();
        string selection = "web";
        int approvals = 0;
        bool allow = true;
        Task<string> Call(string tool, JsonObject args, CancellationToken ct = default) => web.CallAsync(tool, args,
            _ => Task.FromResult(selection), (_, _, _) => { approvals++; return Task.FromResult(allow); }, ct);
        Task<string> Request(string path, string method = "GET", string? body = null, JsonArray? headers = null, CancellationToken ct = default) =>
            Call("web_http_request", new() { ["url"] = server.Url + path, ["method"] = method, ["body"] = body, ["headers"] = headers }, ct);
        async Task Throws<T>(Func<Task> action, string label) where T : Exception
        { try { await action(); } catch (T) { check(true, label); return; } throw new Exception("Expected " + typeof(T).Name + ": " + label); }
        var definitions = new JsonArray(); WebHttpTools.AddDefinitions(definitions, "web");
        check(definitions.Count == 2, "HTTP tools available with Web research alone");
        var disabled = new JsonArray(); WebHttpTools.AddDefinitions(disabled, "browser_access");
        check(disabled.Count == 0, "Browser access alone does not enable direct HTTP");
        var result = JsonNode.Parse(await Request("/hello"))!;
        check(result["status"]!.GetValue<int>() == 200 && result["body"]!.GetValue<string>() == "Bonjour été 世界", "Real .NET HTTP request decodes Unicode without browser");
        check(approvals == 1, "HTTP request asks permission");
        result = JsonNode.Parse(await Request("/echo", "POST", "{\"ok\":true}", new JsonArray(new JsonObject { ["name"] = "Content-Type", ["value"] = "application/json" })))!;
        check(result["body"]!.GetValue<string>().Contains("{\"ok\":true}") && server.LastHeaders.Contains("Content-Type: application/json"), "POST sends UTF-8 body and custom content type");
        result = JsonNode.Parse(await Request("/error"))!;
        check(result["status"]!.GetValue<int>() == 404 && result["body"]!.GetValue<string>() == "missing", "HTTP errors return status and body");
        await Call("web_http_configure", new() { ["max_response_bytes"] = 4 });
        result = JsonNode.Parse(await Request("/hello"))!;
        check(result["truncated"]!.GetValue<bool>() && result["bytes_read"]!.GetValue<int>() == 4, "Response cap enforced while streaming");
        await Call("web_http_configure", new() { ["reset"] = true });
        result = JsonNode.Parse(await Request("/gzip"))!;
        check(result["body"]!.GetValue<string>() == "compressed", "HTTP gzip decompression enabled");
        await Call("web_http_configure", new() { ["decompress"] = false });
        result = JsonNode.Parse(await Request("/gzip"))!;
        check(result["body_encoding"]!.GetValue<string>() == "base64", "Undecoded compressed response remains binary");
        await Call("web_http_configure", new() { ["reset"] = true });
        await Call("web_http_configure", new() { ["user_agent"] = "HttpTest/2.0", ["proxy_url"] = second.Url });
        await Request("/hello");
        check(second.LastHeaders.Contains("User-Agent: HttpTest/2.0"), "Configured proxy and user agent used by .NET client");
        await Call("web_http_configure", new() { ["reset"] = true });
        result = JsonNode.Parse(await Request("/binary"))!;
        check(result["body_encoding"]!.GetValue<string>() == "base64" && result["body"]!.GetValue<string>() == "AAEC/w==", "Binary responses are explicitly base64");
        result = JsonNode.Parse(await Request("/redirect"))!;
        check(result["status"]!.GetValue<int>() == 302, "Redirects default off");
        await Call("web_http_configure", new() { ["follow_redirects"] = true });
        int before = approvals;
        result = JsonNode.Parse(await Request("/redirect"))!;
        check(result["redirects"]!.GetValue<int>() == 1 && approvals == before + 2, "Every redirect target requests approval");
        server.Redirect = second.Url + "/hello";
        await Request("/cross", headers: new JsonArray(new JsonObject { ["name"] = "Authorization", ["value"] = "Bearer test" }, new JsonObject { ["name"] = "X-Key", ["value"] = "test" }));
        check(!second.LastHeaders.Contains("Authorization:") && !second.LastHeaders.Contains("X-Key:"), "Cross-origin redirects strip custom credentials");
        await Throws<HttpRequestException>(() => Request("/loop"), "Redirect loops bounded");
        await Call("web_http_configure", new() { ["use_cookies"] = true });
        await Request("/cookie"); await Request("/hello");
        check(server.LastHeaders.Contains("Cookie: session=test"), "Optional cookies retained within run");
        await Call("web_http_configure", new() { ["reset"] = true }); await Request("/hello");
        check(!server.LastHeaders.Contains("Cookie:"), "Reset clears cookies");
        int received = server.Count; allow = false;
        check((await Request("/hello")).Contains("permission denied") && received == server.Count, "Denied request sends no traffic");
        allow = true; selection = "";
        await Throws<UnauthorizedAccessException>(() => Request("/hello"), "Disabled skill blocks calls");
        selection = "web";
        await Throws<UnauthorizedAccessException>(() => web.CallAsync("web_http_request", new() { ["url"] = server.Url }, _ => Task.FromResult(selection), (_, _, _) => { selection = ""; return Task.FromResult(true); }, default), "Skill rechecked after approval");
        selection = "web";
        await Throws<ArgumentException>(() => Call("web_http_request", new() { ["url"] = "file:///etc/passwd" }), "Non-HTTP schemes rejected");
        await Throws<ArgumentException>(() => Call("web_http_request", new() { ["url"] = "http://user:secret@localhost/" }), "Embedded URL credentials rejected");
        await Throws<ArgumentException>(() => Request("/hello", headers: new JsonArray(new JsonObject { ["name"] = "X-Test", ["value"] = "ok\r\nInjected: yes" })), "Header injection rejected");
        await Throws<ArgumentException>(() => Call("web_http_configure", new() { ["timeout_seconds"] = 0 }), "Invalid configuration rejected");
        await Call("web_http_configure", new() { ["timeout_seconds"] = 1 });
        await Throws<TimeoutException>(() => Request("/slow"), "Slow response times out");
        using var cancel = new CancellationTokenSource(50);
        await Throws<OperationCanceledException>(() => Request("/slow", ct: cancel.Token), "Cancellation interrupts requests");
        foreach (var name in new[] { "web_http_request", "web_http_configure" })
            check(!AgentPolicy.Allowed("plan", name) && !SandboxWorkspace.Allowed(name), "HTTP respects Plan/sandbox restrictions: " + name);
        check(Skills.Prompt("web", "en", hasBrowser: false).Contains("web_http_request"), "HTTP instructions remain available without browser");
        // Shared dispatcher used by both GUI and CLI must register and enforce the feature.
        var path = Path.Combine(Path.GetTempPath(), "omh-http-" + Guid.NewGuid().ToString("N") + ".sqlite");
        using var run = new ConversationSession(new Chat { ExecutionMode = "execute" }, new Project(), new Provider(), new AppState { EnabledSkills = "web" }, "", [], path);
        var runtime = new AgentRuntime(run, new CustomSkills(Path.Combine(Path.GetTempPath(), "absent-http-skills")), (_, _, _) => throw new NotSupportedException(), (_, _, _) => Task.FromResult(true), _ => Task.CompletedTask);
        var registered = new JsonArray(); runtime.AddDefinitions(registered);
        check(registered.Any(x => x?["function"]?["name"]?.GetValue<string>() == "web_http_request"), "Shared GUI/CLI runtime exposes HTTP tools");
        run.Chat.ExecutionMode = "plan";
        await Throws<UnauthorizedAccessException>(() => runtime.CallAsync("web_http_request", new() { ["url"] = server.Url }, default), "Runtime blocks HTTP in Plan even on direct dispatch");
    }

    sealed class Server : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource stop = new();
        readonly Task loop;
        public string Url { get; }
        public string Redirect = "";
        public string LastHeaders = "";
        public int Count;
        public Server() { listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port; loop = Accept(); }
        async Task Accept()
        {
            try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); _ = Respond(client); } }
            catch (OperationCanceledException) { }
        }
        async Task Respond(TcpClient client)
        {
            using (client)
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var first = await reader.ReadLineAsync(stop.Token) ?? "";
                var headers = new List<string>(); string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(stop.Token))) headers.Add(line);
                LastHeaders = string.Join("\n", headers); Interlocked.Increment(ref Count);
                var size = headers.FirstOrDefault(x => x.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))?.Split(':')[1].Trim();
                var content = new char[size == null ? 0 : int.Parse(size)];
                if (content.Length > 0) await reader.ReadBlockAsync(content.AsMemory(), stop.Token);
                var path = first.Split(' ').ElementAtOrDefault(1) ?? "";
                string status = "200 OK", extra = "", media = "text/plain; charset=utf-8";
                byte[] body = Encoding.UTF8.GetBytes("Bonjour été 世界");
                switch (path)
                {
                    case "/slow": await Task.Delay(5000, stop.Token); break;
                    case "/echo": body = Encoding.UTF8.GetBytes(content); break;
                    case "/error": status = "404 Not Found"; body = Encoding.UTF8.GetBytes("missing"); break;
                    case "/redirect": status = "302 Found"; extra = "Location: /hello\r\n"; break;
                    case "/cross": status = "302 Found"; extra = "Location: " + Redirect + "\r\n"; break;
                    case "/loop": status = "302 Found"; extra = "Location: /loop\r\n"; break;
                    case "/cookie": extra = "Set-Cookie: session=test; Path=/\r\n"; break;
                    case "/binary": media = "application/octet-stream"; body = [0, 1, 2, 255]; break;
                    case "/gzip":
                        using (var buffer = new MemoryStream()) { using (var zip = new GZipStream(buffer, CompressionMode.Compress, true)) zip.Write(Encoding.UTF8.GetBytes("compressed")); body = buffer.ToArray(); }
                        extra = "Content-Encoding: gzip\r\n"; break;
                }
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {media}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n{extra}\r\n"), stop.Token);
                await stream.WriteAsync(body, stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
        }
        public void Dispose() { stop.Cancel(); listener.Stop(); loop.GetAwaiter().GetResult(); }
    }
}
