using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace OhMyHarness.Core;

public static class ConversationExport
{
    public const int ClipboardLimitBytes = 512 * 1024;
    public sealed record Progress(int MessageId, GenerationUpdate Update);
    public sealed record Document(string FileName, string Markdown, bool UseClipboard);

    public static async Task<Document> CreateAsync(HarnessDb db, int chatId, int providerId,
        bool running, bool browser, bool dom, Progress? progress = null)
    {
        var chat = await db.Chats.AsNoTracking().SingleAsync(x => x.Id == chatId);
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == chat.ProjectId);
        var state = await db.States.AsNoTracking().SingleAsync();
        var provider = await db.Providers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == providerId);
        var messages = await db.Messages.AsNoTracking().Include(x => x.Attachments).Where(x => x.ChatId == chatId).OrderBy(x => x.Id).ToListAsync();
        var servers = await db.McpServers.AsNoTracking().ToListAsync();
        return Build(chat, project, state, provider, messages, servers, running, browser, dom, progress);
    }

    public static Document Build(Chat chat, Project project, AppState state, Provider? provider,
        IEnumerable<Message> messages, IEnumerable<McpServer> servers, bool running, bool browser, bool dom, Progress? progress = null)
    {
        string L(string fr, string en) => state.Language == "en" ? en : fr;
        var b = new StringBuilder();
        void Setting(string label, object? value) => b.AppendLine($"- **{label}** : {value}");
        b.AppendLine("# " + chat.Title).AppendLine();
        Setting(L("Projet", "Project"), project.Name);
        Setting(L("Export UTC", "Export UTC"), DateTimeOffset.UtcNow.ToString("O"));
        b.AppendLine().AppendLine("## " + L("Réglages au moment de l’export", "Settings at export time")).AppendLine();
        b.AppendLine(L("Ces réglages sont actuels ; leur historique par message n’est pas enregistré. Les clés API, mots de passe et secrets de configuration MCP sont exclus.",
            "These are current settings; per-message settings history is not recorded. API keys, passwords and MCP configuration secrets are excluded.")).AppendLine();
        Setting(L("Fournisseur", "Provider"), provider?.Name ?? "—");
        Setting("Type", provider?.Kind ?? "—");
        Setting(L("Modèle", "Model"), provider?.Model ?? "—");
        if (provider != null)
        {
            var endpoint = Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var uri)
                ? new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.GetLeftPart(UriPartial.Path) : "—";
            Setting("API", endpoint);
            Setting(L("Limite de contexte", "Context limit"), provider.ContextLimit);
            Setting(L("Images acceptées", "Image support"), provider.SupportsImages);
            Setting("OpenCode auto-start / tools", $"{provider.AutoStart} / {provider.OpenCodeTools}");
        }
        Setting(L("Langue", "Language"), state.Language);
        Setting("Thinking", state.ThinkingLevel);
        Setting(L("Détails du raisonnement", "Reasoning details"), state.ShowReasoningDetails);
        Setting("Mode", chat.ExecutionMode);
        Setting(L("Sous-agents", "Subagents"), chat.OrchestrationMode);
        Setting("Sandbox", chat.SandboxEnabled);
        Setting(L("Autorisations", "Permissions"), state.PermissionMode);
        Setting(L("Continuation automatique", "Auto-continue"), state.AutoContinue);
        Setting("Skills", state.EnabledSkills);
        Setting(L("Accès navigateur / DOM", "Browser / DOM access"), $"{browser} / {dom}");
        Setting(L("Sources associées", "Attached sources"), string.Join(", ", project.GetSourceFolders()));
        foreach (var server in servers)
            Setting("MCP", $"{server.Name} · {server.Transport} · {L("activé", "enabled")}={server.Enabled}");
        b.AppendLine().AppendLine("## " + L("Échanges", "Conversation")).AppendLine();
        if (running) b.AppendLine("> " + L("Génération en cours : instantané partiel, sans arrêter l’agent.", "Generation in progress: partial snapshot; the agent continues running.")).AppendLine();
        foreach (var message in messages)
        {
            var live = message.State != "complete" && progress?.MessageId == message.Id ? progress.Update : null;
            b.AppendLine($"### #{message.Id} — {message.Role}").AppendLine();
            Setting(L("État", "State"), message.State);
            Setting("Tokens (input / output)", $"{live?.InputTokens ?? message.InputTokens} / {live?.OutputTokens ?? message.OutputTokens}");
            Setting(L("Durée (s)", "Duration (s)"), (live?.Seconds ?? message.Seconds).ToString("0.###", CultureInfo.InvariantCulture));
            JsonObject? wire = null;
            try { if (!string.IsNullOrWhiteSpace(message.WireJson)) wire = JsonNode.Parse(message.WireJson) as JsonObject; } catch (System.Text.Json.JsonException) { }
            var reasoning = live?.Reasoning ?? wire?["reasoning_content"]?.ToString() ?? wire?["reasoning"]?.ToString();
            if (!string.IsNullOrEmpty(reasoning)) b.AppendLine().AppendLine("#### " + L("Raisonnement", "Reasoning")).AppendLine().AppendLine(reasoning);
            b.AppendLine().AppendLine(live?.Text ?? message.Content).AppendLine();
            if (wire?["tool_calls"] is { } calls) b.AppendLine("#### " + L("Appels d’outils", "Tool calls")).AppendLine().AppendLine(Fence(calls.ToJsonString(), "json"));
            if (wire?["tool_call_id"] is { } callId) Setting("Tool call ID", callId.ToString());
            foreach (var attachment in message.Attachments)
            {
                b.AppendLine().AppendLine($"**{L("Pièce jointe", "Attachment")} : {attachment.Name}** ({attachment.Mime}, {attachment.Data.Length} bytes)");
                if (attachment.Mime is "image/png" or "image/jpeg" or "image/webp" or "image/gif")
                    b.AppendLine($"![image](data:{attachment.Mime};base64,{Convert.ToBase64String(attachment.Data)})");
                else b.AppendLine(Fence(Convert.ToBase64String(attachment.Data), "base64"));
            }
            b.AppendLine().AppendLine("---").AppendLine();
        }
        var markdown = b.ToString();
        var title = Regex.Replace(chat.Title, "[^\\p{L}\\p{N}_-]+", "-").Trim('-');
        if (title.Length > 60) title = title[..60];
        return new($"conversation-{chat.Id}-{title}.md", markdown, Encoding.UTF8.GetByteCount(markdown) <= ClipboardLimitBytes);
    }

    static string Fence(string text, string language)
    {
        var size = Math.Max(3, Regex.Matches(text, "`+").Select(x => x.Length + 1).DefaultIfEmpty(3).Max());
        var fence = new string('`', size);
        return $"{fence}{language}\n{text}\n{fence}";
    }
}
