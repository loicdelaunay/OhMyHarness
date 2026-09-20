using OhMyHarness.Core;
using System.Text.Json.Nodes;

static class WorkflowChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var guard = new ToolLoopGuard();
        check(!guard.Observe("read", "{\"a\":1,\"b\":2}") && !guard.Observe("read", "{\"b\":2,\"a\":1}") && guard.Observe("read", "{ \"a\":1, \"b\":2 }"), "Troisième appel identique détecté indépendamment de l'ordre JSON");
        check(!guard.Observe("read", "{\"a\":2}") && !guard.Observe("other", "{\"a\":2}"), "Autres paramètres ou outil réinitialisent la série");
        int decisions = 0;
        var workflow = new WorkflowTools((q, ct) => { decisions++; return Task.FromResult(new AgentAnswer(false, new List<IReadOnlyList<string>> { new[] { "Continuer une fois / Continue once" } })); }, (_, _) => Task.CompletedTask);
        guard = new();
        for (int i = 0; i < 4; i++) await guard.CheckAsync("same", "{}", workflow, default);
        check(decisions == 2, "Continuer une fois redemande au prochain appel identique");
        var denied = new WorkflowTools((_, _) => Task.FromResult(new AgentAnswer(true, [])), (_, _) => Task.CompletedTask);
        bool stopped = false;
        try { await guard.CheckAsync("same", "{}", denied, default); } catch (OperationCanceledException) { stopped = true; }
        check(stopped, "Annuler une décision de boucle arrête le travail");
        var q = WorkflowTools.ParseQuestions(JsonNode.Parse("""[{"question":"Choisir","options":[{"label":"A","description":"Option A"}],"custom":false}]""")!.AsArray());
        bool invalid = false;
        try { WorkflowTools.ValidateAnswer(q, new(false, [new[] { "forged" }])); } catch (ArgumentException) { invalid = true; }
        check(invalid, "Choix inconnu refusé lorsque le texte libre est désactivé");
        invalid = false;
        try { WorkflowTools.ValidateTasks(JsonNode.Parse("""[{"content":"test","status":"invented"}]""")!.AsArray()); } catch (ArgumentException) { invalid = true; }
        check(invalid, "Statut de tâche invalide refusé");
        check(AgentPolicy.Allowed("plan", "question") && AgentPolicy.Allowed("plan", "todowrite"), "Questions et liste de tâches disponibles en Plan");

        int reads = 0, asked = 0, saves = 0, statusReads = 0;
        bool replied = false, loopReplied = false, aborted = false;
        JsonObject? rules = null;
        using var client = new HttpClient(new FakeHandler(async request => {
            var path = request.RequestUri!.AbsolutePath;
            var json = "{}";
            if (request.Method == HttpMethod.Patch) rules = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            else if (path.EndsWith("/prompt_async")) check(JsonNode.Parse(await request.Content!.ReadAsStringAsync())?["tools"] == null, "OpenCode ne remplace pas les règles doom_loop par des booléens");
            else if (path.EndsWith("/permission")) json = loopReplied ? "[]" : """[{"id":"loop","sessionID":"session","permission":"doom_loop","patterns":["read"]}]""";
            else if (path.EndsWith("/permissions/loop")) { loopReplied = true; check(JsonNode.Parse(await request.Content!.ReadAsStringAsync())?["response"]?.GetValue<string>() == "once", "Boucle OpenCode : autorisation ponctuelle seulement"); }
            else if (path.EndsWith("/question")) json = replied ? "[]" : """[{"id":"other","sessionID":"other-session","questions":[{"question":"Ignore","options":[]}]},{"id":"ask","sessionID":"session","questions":[{"question":"Couleur ?","options":[{"label":"Bleu","description":""}]}]}]""";
            else if (path.EndsWith("/question/ask/reply")) { replied = true; check(JsonNode.Parse(await request.Content!.ReadAsStringAsync())?["answers"]?[0]?[0]?.GetValue<string>() == "Bleu", "Réponse au formulaire envoyée à OpenCode"); }
            else if (path.EndsWith("/question/ask/reject")) { replied = true; }
            else if (path.EndsWith("/todo")) json = """[{"content":"Inspecter","status":"completed","priority":"high"}]""";
            else if (path.EndsWith("/message")) json = ++reads == 1 ? "[]" : """[{"info":{"id":"response","role":"assistant","time":{"completed":1}},"parts":[{"type":"text","text":"Done"}]}]""";
            else if (path.EndsWith("/session/status")) json = ++statusReads == 1 ? """{"session":{"type":"busy"}}""" : "{}";
            else if (path.EndsWith("/abort")) aborted = true;
            else throw new Exception(path);
            return new(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) };
        }));
        var relay = new WorkflowTools((items, _) => { asked++; return Task.FromResult(new AgentAnswer(false, [new[] { items[0].Question.StartsWith("Boucle") ? "Continuer une fois / Continue once" : "Bleu" }])); }, (_, _) => { saves++; return Task.CompletedTask; });
        var provider = new Provider { Kind = "opencode", BaseUrl = "http://127.0.0.1:4096", Model = "test/model", OpenCodeTools = true };
        await new OpenCodeEngine(client).PromptAsync(provider, "", "test", "session", "test", "test", [], _ => { }, default, policy: new("plan", "disabled"), workflow: relay);
        check(rules?["permission"]?.AsArray().Any(x => x?["permission"]?.GetValue<string>() == "doom_loop" && x?["action"]?.GetValue<string>() == "ask") == true, "OpenCode impose doom_loop ask dans la session");
        check(rules?["permission"]?.AsArray().Any(x => x?["permission"]?.GetValue<string>() == "question" && x?["action"]?.GetValue<string>() == "allow") == true, "OpenCode active question même en Plan");
        check(asked == 2 && replied && loopReplied && saves == 1 && statusReads == 2 && !aborted, "Relais OpenCode isolé par session, tâches dédupliquées et attente de la fin réelle");
        loopReplied = false; reads = 0;
        bool loopStopped = false;
        try { await new OpenCodeEngine(client).PromptAsync(provider, "", "test", "session", "test", "test", [], _ => { }, default, workflow: denied); }
        catch (OperationCanceledException) { loopStopped = true; }
        check(loopStopped && aborted, "Arrêt de boucle OpenCode annule réellement la session distante");
        loopReplied = true; replied = false; reads = 0; statusReads = 1; aborted = false;
        await new OpenCodeEngine(client).PromptAsync(provider, "", "test", "session", "test", "test", [], _ => { }, default, workflow: denied);
        check(replied && !aborted, "Annuler une question OpenCode transmet reject puis reprend la session");
    }
}
