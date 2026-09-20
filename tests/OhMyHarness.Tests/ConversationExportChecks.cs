using OhMyHarness.Core;

static class ConversationExportChecks
{
    public static void Run(Action<bool, string> check)
    {
        var chat = new Chat { Id = 7, Title = "Sujet / test", ExecutionMode = "plan", SandboxEnabled = true };
        var project = new Project { Name = "Export test", SourceFolder = "project-root" };
        var state = new AppState { ThinkingLevel = "high", PermissionMode = "deny", AutoContinue = true, EnabledSkills = "sources,terminal" };
        var provider = new Provider { Name = "Provider", Model = "test-model", ProtectedKey = [1,2,3], BaseUrl = "https://login:PASSWORD@example.com/v1?key=PRIVATE_KEY", Username = "PRIVATE_USER" };
        var messages = new List<Message>
        {
            new() { Id = 1, Role = "user", Content = "Bonjour", Attachments = [new() { Name = "image.png", Data = [1,2,3], Mime = "image/png" }] },
            new() { Id = 2, Role = "assistant", Content = "Réponse", WireJson = """{"reasoning_content":"Analyse conservée","tool_calls":[{"id":"call-test","function":{"name":"read_source","arguments":"{}"}}]}""" },
            new() { Id = 3, Role = "tool", Content = "Résultat outil", WireJson = """{"tool_call_id":"call-test"}""" },
            new() { Id = 4, Role = "compaction", Content = "Résumé conservé" },
            new() { Id = 5, Role = "assistant", Content = "persisted", State = "interrupted" }
        };
        var mcp = new McpServer { Name = "Local MCP", Enabled = true, Command = "SECRET_COMMAND", ArgumentsJson = "SECRET_ARG", ProtectedSecrets = [4,5,6] };
        var progress = new ConversationExport.Progress(5, new GenerationUpdate("Streaming courant", "Raisonnement courant", 10, 20, 1));
        ConversationExport.Document Export() => ConversationExport.Build(chat, project, state, provider, messages, [mcp], true, true, false, progress);
        var document = Export();
        check(document.UseClipboard && document.FileName == "conversation-7-Sujet-test.md", "Export court vers le presse-papiers avec nom de fichier portable");
        check(new[] { "Bonjour", "Analyse conservée", "read_source", "Résultat outil", "call-test", "Résumé conservé", "data:image/png;base64,AQID", "Streaming courant", "Raisonnement courant" }.All(document.Markdown.Contains), "Export conserve échanges, raisonnement, outils, images, compactage et streaming");
        check(new[] { "test-model", "sources,terminal", "plan", "high", "deny", "Local MCP" }.All(document.Markdown.Contains), "Export inclut les réglages utiles");
        check(new[] { "PASSWORD", "PRIVATE_KEY", "PRIVATE_USER", "SECRET_COMMAND", "SECRET_ARG", "ProtectedKey", "ProtectedSecrets" }.All(x => !document.Markdown.Contains(x)), "Export exclut les secrets des réglages et nettoie l’URL API");
        messages.Add(new() { Id = 6, Content = new string('é', 270_000) });
        check(!Export().UseClipboard, "Export volumineux UTF-8 vers fichier sans troncature");
        state.Language = "en";
        check(Export().Markdown.Contains("Settings at export time"), "Export traduit en anglais");
    }
}
