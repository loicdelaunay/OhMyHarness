using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OhMyHarness.Service;

Console.InputEncoding = Console.OutputEncoding = new UTF8Encoding(false);
var pathIndex = Array.IndexOf(args, "--database");
if (pathIndex < 0 || pathIndex + 1 >= args.Length) throw new ArgumentException("--database requires an absolute SQLite path.");
var database = Path.GetFullPath(args[pathIndex + 1]);
var output = new SemaphoreSlim(1, 1);
var responses = new ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>>();
var pending = new ConcurrentDictionary<string, Task>();
using var lifetime = new CancellationTokenSource();
async Task Write(object value)
{
    var json = JsonSerializer.Serialize(value, HarnessService.Json);
    await output.WaitAsync();
    try { await Console.Out.WriteLineAsync(json); await Console.Out.FlushAsync(); }
    finally { output.Release(); }
}
async Task<JsonNode?> Host(string method, JsonObject parameters, CancellationToken ct)
{
    var id = Guid.NewGuid().ToString("N");
    var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
    responses[id] = completion;
    try
    {
        await Write(new { hostRequest = id, method, parameters });
        return await completion.Task.WaitAsync(ct);
    }
    finally { responses.TryRemove(id, out _); }
}
await using var service = new HarnessService(database, Host, Write);
await service.Initialize();
await Write(new { ready = true });
try
{
    while (await Console.In.ReadLineAsync(lifetime.Token) is { } line)
    {
        JsonObject? request;
        try { request = JsonNode.Parse(line) as JsonObject; }
        catch { continue; }
        if (request == null) continue;
        if (request["hostResponse"]?.GetValue<string>() is { } hostId)
        {
            if (responses.TryGetValue(hostId, out var response))
            {
                if (request["error"]?.GetValue<string>() is { } error) response.TrySetException(new IOException(error));
                else response.TrySetResult(request["result"]?.DeepClone());
            }
            continue;
        }
        var id = request["id"]?.GetValue<string>();
        if (id == null) continue;
        var captured = request;
        var task = Task.Run(async () =>
        {
            try
            {
                var result = await service.Dispatch(captured["method"]?.GetValue<string>() ?? "", captured["parameters"] as JsonObject ?? [], lifetime.Token);
                await Write(new { id, result });
            }
            catch (Exception ex) { await Write(new { id, error = ex is OperationCanceledException ? "Génération arrêtée / Generation stopped." : ex.Message }); }
        });
        pending[id] = task;
        _ = task.ContinueWith(_ => pending.TryRemove(id, out var ignored), TaskScheduler.Default);
    }
}
finally
{
    lifetime.Cancel(); service.CancelAll();
    try { await Task.WhenAll(pending.Values).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
}
