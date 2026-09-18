using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public sealed record OpenCodeModel(string Reference, string Name, int? ContextLimit, bool SupportsImages);
public sealed record OpenCodeAttachment(string Name, string Mime, byte[] Data);
public sealed record OpenCodePermission(string Id, string SessionId, string Action, IReadOnlyList<string> Resources, string Details);

public sealed class OpenCodeEngine(HttpClient http)
{
    static Uri Endpoint(Provider provider, string resource, string? directory = null)
    {
        var suffix = resource.TrimStart('/');
        if (!string.IsNullOrWhiteSpace(directory))
            suffix += (suffix.Contains('?') ? "&" : "?") + "directory=" + Uri.EscapeDataString(directory);
        return ChatEngine.Endpoint(provider.BaseUrl, suffix);
    }

    static void Configure(HttpRequestMessage request, Provider provider, string password, string? directory)
    {
        if (!string.IsNullOrEmpty(password))
        {
            var user = string.IsNullOrWhiteSpace(provider.Username) ? "opencode" : provider.Username.Trim();
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));
        }
        if (!string.IsNullOrWhiteSpace(directory)) request.Headers.TryAddWithoutValidation("x-opencode-directory", directory);
    }

    async Task<JsonNode?> JsonAsync(Provider provider, string password, HttpMethod method, string resource, string? directory, JsonNode? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, Endpoint(provider, resource, directory));
        Configure(request, provider, password, directory);
        if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenCode : HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {ErrorText(content)}".Trim());
        return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
    }

    static string ErrorText(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "";
        try
        {
            var json = JsonNode.Parse(content);
            return json?["message"]?.GetValue<string>() ?? json?["error"]?.ToJsonString() ?? content[..Math.Min(500, content.Length)];
        }
        catch { return content[..Math.Min(500, content.Length)]; }
    }

    public async Task<string> HealthAsync(Provider provider, string password, CancellationToken ct)
    {
        var json = await JsonAsync(provider, password, HttpMethod.Get, "global/health", null, null, ct);
        if (json?["healthy"]?.GetValue<bool>() != true) throw new IOException("Le serveur OpenCode ne se déclare pas disponible.");
        return json?["version"]?.GetValue<string>() ?? "inconnue";
    }

    public async Task<List<OpenCodeModel>> ModelsAsync(Provider provider, string password, string? directory, CancellationToken ct)
    {
        var json = await JsonAsync(provider, password, HttpMethod.Get, "provider", directory, null, ct);
        var providers = json?["all"] as JsonArray ?? json?["providers"] as JsonArray ?? json as JsonArray ?? [];
        var connected = (json?["connected"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<OpenCodeModel>();
        foreach (var item in providers.OfType<JsonObject>())
        {
            var providerId = String(item, "id", "providerID", "key");
            if (providerId.Length == 0) continue;
            var isConnected = connected is { Count: > 0 } && connected.Contains(providerId);
            var isFreeProvider = providerId.Equals("opencode", StringComparison.OrdinalIgnoreCase) ||
                                 providerId.Equals("opencode-go", StringComparison.OrdinalIgnoreCase) ||
                                 providerId.Contains("zen", StringComparison.OrdinalIgnoreCase);

            if (item["models"] is JsonObject modelMap)
            {
                foreach (var pair in modelMap)
                {
                    var model = pair.Value as JsonObject;
                    if (isConnected || isFreeProvider || IsFreeModel(model, pair.Key))
                        AddModel(result, providerId, pair.Key, model);
                }
            }
            else if (item["models"] is JsonArray modelList)
            {
                foreach (var model in modelList.OfType<JsonObject>())
                {
                    var modelId = String(model, "id", "modelID");
                    if (isConnected || isFreeProvider || IsFreeModel(model, modelId))
                        AddModel(result, providerId, modelId, model);
                }
            }
        }
        return result.GroupBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
                     .Select(x => x.First())
                     .OrderByDescending(x => x.Reference.StartsWith("opencode/", StringComparison.OrdinalIgnoreCase))
                     .ThenBy(x => x.Reference)
                     .ToList();
    }

    public static bool IsFreeModel(JsonObject? model, string modelId)
    {
        if (modelId.Contains("-free", StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains("free", StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains("big-pickle", StringComparison.OrdinalIgnoreCase))
            return true;
        if (model == null) return false;
        var name = String(model, "name", "label");
        if (name.Contains("free", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("gratuit", StringComparison.OrdinalIgnoreCase))
            return true;
        if (model["cost"] is JsonObject cost)
        {
            var input = Int(cost["input"]) ?? (int?)(cost["input"] is JsonValue iv && iv.TryGetValue<double>(out var d) ? (int)d : null);
            var output = Int(cost["output"]) ?? (int?)(cost["output"] is JsonValue ov && ov.TryGetValue<double>(out var d2) ? (int)d2 : null);
            if (input == 0 && output == 0) return true;
        }
        return false;
    }

    static void AddModel(List<OpenCodeModel> result, string providerId, string modelId, JsonObject? model)
    {
        if (modelId.Length == 0) return;
        var name = model == null ? modelId : String(model, "name", "label");
        var context = Int(model?["limit"]?["context"]) ?? Int(model?["context"]);
        var supportsImages = HasImage(model?["capabilities"]?["input"]) || HasImage(model?["modalities"]?["input"]);
        result.Add(new OpenCodeModel(providerId + "/" + modelId, name.Length == 0 ? modelId : name, context, supportsImages));
    }

    static bool HasImage(JsonNode? node) => node is JsonArray array && array.Any(x => string.Equals(x?.GetValue<string>(), "image", StringComparison.OrdinalIgnoreCase));
    static int? Int(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<int>(out var integer)) return integer;
        if (value.TryGetValue<double>(out var number)) return (int)number;
        return null;
    }
    static string String(JsonObject? value, params string[] names)
    {
        if (value == null) return "";
        foreach (var name in names)
            if (value[name] is JsonValue item && item.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) return text;
        return "";
    }

    public async Task<string> CreateSessionAsync(Provider provider, string password, string directory, string title, CancellationToken ct)
    {
        var json = await JsonAsync(provider, password, HttpMethod.Post, "session", directory, new JsonObject { ["title"] = title }, ct);
        var id = json?["id"]?.GetValue<string>();
        return !string.IsNullOrWhiteSpace(id) ? id : throw new IOException("OpenCode n’a pas renvoyé l’identifiant de la session.");
    }

    public async Task<Completion> PromptAsync(Provider provider, string password, string directory, string sessionId, string prompt,
        string systemPrompt, IReadOnlyList<OpenCodeAttachment> attachments, Action<GenerationUpdate> update, CancellationToken ct,
        Func<OpenCodePermission, CancellationToken, Task<string>>? authorize = null)
    {
        var separator = provider.Model.IndexOf('/');
        if (separator <= 0 || separator == provider.Model.Length - 1) throw new ArgumentException("Le modèle OpenCode doit être au format fournisseur/modèle.");
        var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = prompt } };
        foreach (var attachment in attachments)
            parts.Add(new JsonObject { ["type"] = "file", ["mime"] = attachment.Mime, ["filename"] = attachment.Name,
                ["url"] = $"data:{attachment.Mime};base64,{Convert.ToBase64String(attachment.Data)}" });
        var payload = new JsonObject
        {
            ["model"] = new JsonObject { ["providerID"] = provider.Model[..separator], ["modelID"] = provider.Model[(separator + 1)..] },
            ["system"] = systemPrompt,
            ["parts"] = parts
        };
        payload["tools"] = provider.OpenCodeTools
            ? new JsonObject { ["question"] = false }
            : await DisabledToolsAsync(provider, password, directory, ct);
        var before = await MessagesAsync(provider, password, directory, sessionId, ct);
        var known = before.Select(MessageId).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
        var timer = Stopwatch.StartNew();
        try
        {
            await JsonAsync(provider, password, HttpMethod.Post, $"session/{Uri.EscapeDataString(sessionId)}/prompt_async", directory, payload, ct);
            string lastText = "", lastReasoning = "";
            while (true)
            {
                await Task.Delay(180, ct);
                if (provider.OpenCodeTools && authorize != null)
                {
                    foreach (var permission in await PermissionsAsync(provider, password, directory, sessionId, ct))
                    {
                        var response = await authorize(permission, ct);
                        if (response is not ("once" or "always" or "reject")) response = "reject";
                        await JsonAsync(provider, password, HttpMethod.Post,
                            $"session/{Uri.EscapeDataString(sessionId)}/permissions/{Uri.EscapeDataString(permission.Id)}",
                            directory, new JsonObject { ["response"] = response }, ct);
                    }
                }
                var messages = await MessagesAsync(provider, password, directory, sessionId, ct);
                var assistant = messages.LastOrDefault(x => IsAssistant(x) && !known.Contains(MessageId(x)));
                if (assistant == null) continue;
                var parsed = ParseAssistant(assistant, timer.Elapsed.TotalSeconds);
                if (parsed.Text != lastText || parsed.Reasoning != lastReasoning || parsed.Completed)
                {
                    lastText = parsed.Text; lastReasoning = parsed.Reasoning;
                    update(new(parsed.Text, parsed.Reasoning, parsed.InputTokens, parsed.OutputTokens, timer.Elapsed.TotalSeconds));
                }
                if (!parsed.Completed) continue;
                if (parsed.Error.Length > 0) throw new IOException("OpenCode : " + parsed.Error);
                var message = new JsonObject { ["role"] = "assistant", ["content"] = parsed.Text };
                if (parsed.Reasoning.Length > 0) message["reasoning_content"] = parsed.Reasoning;
                return new Completion(message, parsed.InputTokens, parsed.OutputTokens, timer.Elapsed.TotalSeconds);
            }
        }
        catch (OperationCanceledException)
        {
            using var abort = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await JsonAsync(provider, password, HttpMethod.Post, $"session/{Uri.EscapeDataString(sessionId)}/abort", directory, new JsonObject(), abort.Token); } catch { }
            throw;
        }
    }

    async Task<JsonObject> DisabledToolsAsync(Provider provider, string password, string directory, CancellationToken ct)
    {
        var disabled = new JsonObject();
        try
        {
            var json = await JsonAsync(provider, password, HttpMethod.Get, "experimental/tool/ids", directory, null, ct);
            var ids = json as JsonArray ?? json?["ids"] as JsonArray ?? [];
            foreach (var id in ids.Select(x => x?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x))) disabled[id!] = false;
        }
        catch (HttpRequestException) { }
        foreach (var id in new[] { "bash", "edit", "write", "read", "glob", "grep", "webfetch", "websearch", "task", "todowrite" }) disabled[id] = false;
        return disabled;
    }

    async Task<JsonArray> MessagesAsync(Provider provider, string password, string directory, string sessionId, CancellationToken ct) =>
        await JsonAsync(provider, password, HttpMethod.Get, $"session/{Uri.EscapeDataString(sessionId)}/message", directory, null, ct) as JsonArray ?? [];

    async Task<List<OpenCodePermission>> PermissionsAsync(Provider provider, string password, string directory, string sessionId, CancellationToken ct)
    {
        var json = await JsonAsync(provider, password, HttpMethod.Get, "permission", directory, null, ct);
        var items = json as JsonArray ?? json?["permissions"] as JsonArray ?? [];
        var result = new List<OpenCodePermission>();
        foreach (var item in items.OfType<JsonObject>())
        {
            var requestSession = String(item, "sessionID", "sessionId");
            if (!string.Equals(requestSession, sessionId, StringComparison.Ordinal)) continue;
            var id = String(item, "id", "requestID");
            if (id.Length == 0) continue;
            var action = String(item, "permission", "action", "type");
            var resources = new List<string>();
            foreach (var field in new[] { "patterns", "resources" })
                if (item[field] is JsonArray values) resources.AddRange(values.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0));
            if (item["pattern"] is JsonValue pattern && pattern.TryGetValue<string>(out var single) && single.Length > 0) resources.Add(single);
            var details = item["metadata"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "";
            result.Add(new OpenCodePermission(id, requestSession, action.Length == 0 ? "outil" : action, resources, details));
        }
        return result;
    }

    static string MessageId(JsonNode? message) => message?["info"]?["id"]?.GetValue<string>() ?? "";
    static bool IsAssistant(JsonNode? message) => string.Equals(message?["info"]?["role"]?.GetValue<string>(), "assistant", StringComparison.OrdinalIgnoreCase);

    static (string Text, string Reasoning, int? InputTokens, int? OutputTokens, bool Completed, string Error) ParseAssistant(JsonNode message, double seconds)
    {
        var info = message["info"];
        var text = new StringBuilder(); var reasoning = new StringBuilder();
        if (message["parts"] is JsonArray parts)
            foreach (var part in parts)
            {
                var value = part?["text"]?.GetValue<string>() ?? "";
                if (part?["type"]?.GetValue<string>() == "text") text.Append(value);
                else if (part?["type"]?.GetValue<string>() == "reasoning") reasoning.Append(value);
            }
        var tokens = info?["tokens"] ?? info?["metadata"]?["assistant"]?["tokens"];
        var input = Int(tokens?["input"]);
        var output = Int(tokens?["output"]);
        var completed = info?["time"]?["completed"] != null || info?["finish"] != null || info?["metadata"]?["time"]?["completed"] != null;
        var error = info?["error"]?["data"]?["message"]?.GetValue<string>() ?? info?["error"]?["message"]?.GetValue<string>() ?? "";
        return (text.ToString(), reasoning.ToString(), input, output, completed, error);
    }
}
