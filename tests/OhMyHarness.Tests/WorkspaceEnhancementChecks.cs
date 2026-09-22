using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;

static class WorkspaceEnhancementChecks
{
    const string Reply = "data: {\"choices\":[{\"delta\":{\"content\":\"Recovered\"}}]}\n\ndata: [DONE]\n\n";
    public static async Task Run(Action<bool,string> check)
    {
        var provider = new Provider { Kind = "deepseek", Model = "deepseek-v4", BaseUrl = "http://inference.example/v1" };
        var history = new JsonArray(new JsonObject { ["role"] = "assistant", ["content"] = "Tool step", ["reasoning_content"] = "retained reasoning", ["tool_calls"] = new JsonArray(new JsonObject { ["id"] = "a", ["type"] = "function", ["function"] = new JsonObject { ["name"] = "read_source", ["arguments"] = "{}" } }) }, new JsonObject { ["role"] = "tool", ["tool_call_id"] = "a", ["content"] = "result" });
        int requests = 0; JsonObject? last = null; string notice = "";
        using (var http = new HttpClient(new FakeHandler(async r => {
            last = JsonNode.Parse(await r.Content!.ReadAsStringAsync())!.AsObject();
            return ++requests == 1 ? new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"message\":\"Missing reasoning_content in thinking mode\"}}") } : new(HttpStatusCode.OK) { Content = new StringContent(Reply) };
        })))
        {
            var engine = new ChatEngine(http);
            var result = await engine.StreamAsync(provider, "secret-key", history, [], x => notice = x.CompatibilityNotice, default, "high");
            check(requests == 2 && result.Message["content"]!.GetValue<string>() == "Recovered" && notice.Length > 0, "DeepSeek : reprise unique et notice visible après incompatibilité du raisonnement");
            check(last!["thinking"]?["type"]?.GetValue<string>() == "disabled" && last["reasoning_effort"] == null && last["messages"]![0]!["reasoning_content"] == null, "DeepSeek : requête de reprise adaptée sans raisonnement incompatible");
            check(history[0]!["reasoning_content"]!.GetValue<string>() == "retained reasoning", "Fallback : historique original conservé");
            await engine.StreamAsync(provider, "secret-key", history, [], _ => { }, default);
            check(requests == 3 && last!["thinking"]?["type"]?.GetValue<string>() == "disabled", "Fallback mémorisé pour les étapes suivantes");
        }
        requests = 0;
        using (var http = new HttpClient(new FakeHandler(_ => { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":{\"message\":\"Invalid secret-key\"}}") }); })))
        {
            try { await new ChatEngine(http).StreamAsync(provider, "secret-key", history, [], _ => { }, default); throw new Exception("Missing HTTP error"); }
            catch (HttpRequestException ex) { check(requests == 1 && ex.Message.Contains("401") && ex.Message.Contains("Invalid") && !ex.Message.Contains("secret-key"), "Erreur d’authentification précise, clé masquée, aucune boucle de reprise"); }
        }
        requests = 0;
        var failures = new[] { "unsupported reasoning_effort", "unsupported stream_options", "unsupported image_url", "unsupported tools", "Missing reasoning_content in thinking mode" };
        using (var http = new HttpClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(new JsonObject { ["error"] = new JsonObject { ["message"] = failures[Math.Min(requests++,4)] } }.ToJsonString()) }))))
        {
            try { await new ChatEngine(http).StreamAsync(provider, "", history, [], _ => { }, default); throw new Exception("Missing error"); }
            catch (HttpRequestException) { check(requests == 5, "Fallback borné : cinq requêtes maximum même si plusieurs capacités sont refusées"); }
        }
        var compatibility = new ProviderCompatibility(); compatibility.Learn("tools not supported", false); compatibility.DisableImages();
        var payload = new JsonObject { ["tools"] = new JsonArray(), ["messages"] = history.DeepClone() };
        payload["messages"]!.AsArray().Add(ChatEngine.ToWire(new Message { Content = "Image question", Attachments = [new() { Mime = "image/png", Data = [1,2] }] }));
        compatibility.Apply(payload);
        check(payload["tools"] == null && !payload.ToJsonString().Contains("image_url") && !payload.ToJsonString().Contains("tool_call_id") && payload.ToJsonString().Contains("Image question"), "Fallback outils et images : contexte textuel conservé et protocole cohérent");

        var root = Path.Combine(Path.GetTempPath(), "omh-workspace-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var docs = Path.Combine(root,"docs"); Directory.CreateDirectory(docs);
            var source = Path.Combine(docs,"notes.custom"); await File.WriteAllTextAsync(source,"Bonjour\nLigne deux\nLigne trois",Encoding.Unicode);
            check((await DocumentText.ReadAsync(source, default)).Contains("Ligne deux"),"RAG : extension arbitraire et texte UTF-16");
            var binary = Path.Combine(docs,"binary.dat"); await File.WriteAllBytesAsync(binary,[0,1,2,3,0,1,2,3]);
            check((await DocumentText.ReadAsync(binary,default)).Contains("Format binaire"),"RAG : format inconnu décrit explicitement sans prétendre le comprendre");
            var office = Path.Combine(docs,"report.docx");
            using(var zip=ZipFile.Open(office,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open());writer.Write("<document><p><t>Compte rendu français</t></p><p><t>Budget du projet</t></p></document>");}
            var extracted=await DocumentText.ReadAsync(office,default);
            check(extracted.Contains("Compte rendu français") && extracted.Contains("Budget du projet"),"RAG : texte Office extrait de l’archive");
            var pdf=Path.Combine(docs,"report.pdf");await File.WriteAllBytesAsync(pdf,Pdf());
            check((await DocumentText.ReadAsync(pdf,default)).Contains("PDF sample"),"RAG : extraction du texte PDF");
            check(DocumentText.Lines("a\nb\nc",2,3)=="2: b\n3: c","RAG : lecture partielle des lignes extraites");
            var single=new SourceAccess(source);
            check(single.List().Contains("notes.custom") && (await single.ReadAsync(source,default)).Contains("Bonjour"),"Ressource isolée : lecture et catalogue d’un fichier explicitement joint");
            bool denied=false;try{single.Resolve(office);}catch(UnauthorizedAccessException){denied=true;}
            check(denied,"Joindre un fichier ne donne pas accès aux fichiers voisins");
            await File.WriteAllTextAsync(Path.Combine(docs,"Agent.md"),"Use project conventions");
            check((await ProjectInstructions.LoadAsync([docs],default)).Contains("Use project conventions"),"Instructions : Agent.md reconnu");
            var project=new Project(); project.SetSourceFolders([docs]);
            var chat=new Chat { ResourcePathsJson=ProjectResources.Serialize([source]) };
            check(ProjectResources.For(chat,project).SequenceEqual([source]) && ProjectResources.For(new Chat(),project).SequenceEqual([docs]),"Conversation : ressources personnalisées distinctes des dossiers par défaut");
            check(ProjectResources.For(new Chat { ResourcePathsJson="[]" },project).Count==0,"Conversation : liste vide explicite sans réhériter des dossiers du projet");
            await File.WriteAllTextAsync(Path.Combine(docs,"permission.json"),"{\"permissions\":{\"desktop\":\"deny\",\"terminal\":\"allow\",\"rag-api\":\"ask\"}}");
            var rules=await ProjectResources.ReadPermissionsAsync(project);
            check(ProjectResources.AutomaticDecision("allow",rules,"desktop|mouse")==false && ProjectResources.AutomaticDecision("ask",rules,"terminal|x")==true && ProjectResources.AutomaticDecision("allow",rules,"rag-api|x")==null && ProjectResources.AutomaticDecision("deny",rules,"terminal|x")==false,"Permissions projet : deny/ask/allow et priorité de Refuser tout");
            await File.WriteAllTextAsync(Path.Combine(docs,"permission.json"),"{\"permissions\":{\"desktop\":\"allow\"}}");
            check(ProjectResources.Decision(rules,"desktop|mouse")=="deny","Modifier permission.json ne change pas le profil déjà approuvé");
            project.PermissionProfileJson="{\"write_source\":\"deny\",\"read_source\":\"ask\"}";
            bool toolDenied=false;int approvals=0;
            try { await ProjectResources.DemandToolAsync(project,"write_source","",(_,_,_)=>{approvals++;return Task.FromResult(true);},default); } catch(UnauthorizedAccessException){toolDenied=true;}
            await ProjectResources.DemandToolAsync(project,"read_source","",(_,_,_)=>{approvals++;return Task.FromResult(true);},default);
            check(toolDenied && approvals==1,"Permissions projet : refus avant écriture et demande pour la lecture explicitement configurée");

            var database=Path.Combine(root,"database.sqlite");await using var db=new HarnessDb(database);await db.InitializeAsync();
            var stored=await db.Projects.FirstAsync();stored.SetSourceFolders([docs]);stored.PermissionProfileJson=rules;
            var original=await db.Chats.FirstAsync();original.ResourcePathsJson=chat.ResourcePathsJson;original.TodoDismissed=true;
            var user=new Message{ChatId=original.Id,Content="Question",Attachments=[new(){Name="x.png",Mime="image/png",Data=[1,2]}]};db.Messages.Add(user);await db.SaveChangesAsync();
            var final=new Message{ChatId=original.Id,Role="assistant",Content="Answer",CompatibilityNotice="Text fallback"};db.Messages.Add(final);await db.SaveChangesAsync();
            user.State="compacted";await db.SaveChangesAsync();
            var fork=await ConversationBranches.CreateAsync(database,original.Id,user.Id,false);
            var forkMessages=await db.Messages.AsNoTracking().Include(x=>x.Attachments).Where(x=>x.ChatId==fork.Id).ToListAsync();
            check(forkMessages.Count==1 && forkMessages[0].State=="complete" && forkMessages[0].Attachments.Single().Data.SequenceEqual(new byte[]{1,2}) && fork.ResourcePathsJson==chat.ResourcePathsJson,"Fork : message compacté restauré, pièces jointes et ressources copiées");
            check(await db.Messages.CountAsync(x=>x.ChatId==original.Id)==2,"Fork : conversation d’origine intacte");
            await ConversationInbox.AddAsync(database,original.Id,(await db.Providers.FirstAsync()).Id,"queued",[],"queued");
            db.Subagents.Add(new(){ChatId=original.Id,Name="Child",Status="completed",TranscriptJson="[]"});await db.SaveChangesAsync();
            await ConversationBranches.CreateAsync(database,original.Id,user.Id,true);
            check(await db.Messages.CountAsync(x=>x.ChatId==original.Id)==1 && !await db.PendingInputs.AnyAsync(x=>x.ChatId==original.Id),"Reprendre : historique limité et file d’attente vidée");
            var backup=await db.Chats.SingleAsync(x=>x.Title.StartsWith("Sauvegarde"));
            check(await db.Subagents.AnyAsync(x=>x.ChatId==backup.Id) && !await db.Subagents.AnyAsync(x=>x.ChatId==original.Id),"Reprendre : les sous-agents précédents sont conservés dans la sauvegarde");
            check(await db.Messages.AnyAsync(x=>x.ChatId==backup.Id && x.Content=="Answer" && x.CompatibilityNotice=="Text fallback"),"Reprendre : suite et notice conservées dans une sauvegarde");
            var state=await db.States.SingleAsync();state.EnabledSkills="rag";await db.SaveChangesAsync();
            db.RagChunks.Add(new(){ProjectId=stored.Id,Model=LocalEmbeddings.ModelId,Path=office,Hash="test",Text="not allowed"});
            db.RagChunks.Add(new(){ProjectId=stored.Id,Model=LocalEmbeddings.ModelId,Path=source,Hash="test",Text="allowed"});await db.SaveChangesAsync();
            using var run=new ConversationSession(original,stored,await db.Providers.FirstAsync(),state,"test",[],database);
            var sources=await RagTools.CallAsync(run,"rag_sources",[],(_,_)=>Task.FromResult(""),(_,_,_,_)=>Task.FromResult(false),default);
            check(sources.Contains("notes.custom") && !sources.Contains("report.docx"),"RAG : le catalogue respecte le périmètre de chaque conversation");
            check((await db.Chats.AsNoTracking().SingleAsync(x=>x.Id==original.Id)).TodoDismissed,"Migration : préférence de fermeture TODO conservée");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true); }
    }
    static byte[] Pdf()
    {
        var objects=new[]{"<< /Type /Catalog /Pages 2 0 R >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>","<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"};
        const string stream="BT /F1 12 Tf 50 700 Td (PDF sample) Tj ET";var text=new StringBuilder("%PDF-1.4\n");var offsets=new List<int>{0};
        foreach(var obj in objects.Append($"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream")){offsets.Add(text.Length);text.Append($"{offsets.Count-1} 0 obj\n{obj}\nendobj\n");}
        int xref=text.Length;text.Append("xref\n0 6\n0000000000 65535 f \n");foreach(var offset in offsets.Skip(1))text.Append($"{offset:D10} 00000 n \n");text.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");return Encoding.ASCII.GetBytes(text.ToString());
    }
}
