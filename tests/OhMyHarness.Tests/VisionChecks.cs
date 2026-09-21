using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using System.Net;
using System.Text.Json.Nodes;

static class VisionChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var folder=Path.Combine(Path.GetTempPath(),"omh-vision-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var file=Path.Combine(folder,"database.sqlite");
        try
        {
            await using var db=new HarnessDb(file);await db.InitializeAsync();
            var chat=await db.Chats.FirstAsync();var project=await db.Projects.FirstAsync();project.SetSourceFolders([folder]);
            var main=await db.Providers.FirstAsync();main.SupportsImages=false;
            var vision=new Provider{Name="Vision provider",BaseUrl="https://vision.example/v1",Model="other",ProtectedKey=[7]};db.Providers.Add(vision);await db.SaveChangesAsync();
            var state=await db.States.SingleAsync();state.EnabledSkills="vision_bridge";state.FeaturesJson=new FeatureSettings{VisionProviderId=vision.Id,VisionModel="vision-test"}.Json();await db.SaveChangesAsync();
            using var run=new ConversationSession(chat,project,main,state,"What is visible?",[],file);
            int requests=0,approvals=0;bool allow=true;JsonObject? payload=null;
            using var http=new HttpClient(new FakeHandler(async request=>
            {
                requests++;payload=JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
                check(request.RequestUri!.Host=="vision.example" && request.Headers.Authorization!.Parameter=="vision-key","Vision : clé et endpoint du fournisseur dédié");
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"A red circle, uncertain label.\"}}]}\n\ndata: [DONE]\n\n")};
            }));
            var bridge=new VisionBridge(run,http,(_,_)=>Task.FromResult("vision-key"),(scope,title,detail,ct)=>{approvals++;return Task.FromResult(allow);});
            var image=new Attachment{Name="sample.png",Mime="image/png",Data=[1,2,3]};
            var wire=new JsonArray(ChatEngine.ToWire(new Message{Content="Question",Attachments=[image]}));
            var output=await bridge.PrepareAsync(wire,default);
            check(!output.ToJsonString().Contains("image_url") && output[0]!["content"]!.GetValue<string>().Contains("red circle") && wire.ToJsonString().Contains("image_url"),"Vision : texte transmis au modèle aveugle sans altérer les images originales");
            check(payload!["model"]!.GetValue<string>()=="vision-test" && payload["tools"]==null && payload.ToJsonString().Contains("data:image/png;base64,AQID"),"Vision : modèle choisi, image reçue, aucun outil délégué");
            await bridge.PrepareAsync(wire,default);check(requests==1 && approvals==1,"Vision : pas de nouvelle analyse aux étapes suivantes du même envoi");
            allow=false;bool denied=false;try{await bridge.AnalyzeAsync(image,"Different question",default);}catch(UnauthorizedAccessException){denied=true;}
            check(denied && requests==1,"Vision : refus bloque l’appel API");allow=true;
            var saved=new Message{ChatId=chat.Id,Content="attached",Attachments=[new(){Name="saved.png",Mime="image/png",Data=[1,2,3]}]};db.Messages.Add(saved);
            var other=new Chat{ProjectId=project.Id};db.Chats.Add(other);await db.SaveChangesAsync();db.Messages.Add(new(){ChatId=other.Id,Content="private",Attachments=[new(){Name="private.png",Mime="image/png",Data=[9]}]});await db.SaveChangesAsync();
            var listing=await bridge.CallAsync("list_images",[],default);check(listing.Contains("saved.png")&&!listing.Contains("private.png"),"Vision : catalogue limité à la conversation");
            bool cross=false;try{await bridge.CallAsync("analyze_image",new(){["image_id"]="999999:0",["question"]="read"},default);}catch(ArgumentException){cross=true;}
            check(cross && requests==1,"Vision : identifiant hors conversation refusé avant transmission");
            await File.WriteAllBytesAsync(Path.Combine(folder,"source.png"),[5,6]);
            check((await bridge.CallAsync("analyze_image",new(){["path"]="source.png",["question"]="Read the label"},default)).Contains("red circle"),"Vision : image du projet analysée avec question ciblée");
            state.EnabledSkills="";await db.SaveChangesAsync();bool disabled=false;try{await bridge.PrepareAsync(wire,default);}catch(UnauthorizedAccessException){disabled=true;}
            check(disabled,"Vision : désactivation du skill respectée même avec cache");
            var definitions=new JsonArray();VisionBridge.AddDefinitions(definitions,"vision_bridge");AgentPolicy.Filter(definitions,"plan");check(definitions.Count==2,"Vision : lecture autorisée en mode Plan");
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
    }
}
