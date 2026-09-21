using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.Service;
public sealed partial class HarnessService
{
    static List<Attachment> InputImages(JsonObject p)
    {
        var images=(p["images"] as JsonArray??[]).Select(x=>new Attachment{Name=x!["name"]!.GetValue<string>(),Mime=x["mime"]!.GetValue<string>(),Data=Convert.FromBase64String(x["data"]!.GetValue<string>())}).ToList();
        if(images.Count>4||images.Any(x=>x.Data.Length>8*1024*1024||x.Mime is not ("image/png" or "image/jpeg" or "image/webp")))throw new ArgumentException("4 PNG/JPEG/WebP images maximum, 8 MB each.");
        return images;
    }
    async Task<object> Inbox(JsonObject p,CancellationToken ct)
    {
        var id=I(p,"chatId");await using var db=Db();
        return await db.PendingInputs.Where(x=>x.ChatId==id).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ChatId,x.Text,x.Mode}).ToListAsync(ct);
    }
    async Task NotifyInbox(int id)=>await emit(new{@event="inbox",chatId=id,items=await Inbox(new(){["chatId"]=id},CancellationToken.None)});
    async Task<object> UpdateInbox(JsonObject p, CancellationToken ct)
    {
        var id = I(p, "chatId"); bool steer = B(p, "steer"); int? providerId = null;
        if (steer)
        {
            if (!runs.TryGetValue(id, out var active)) throw new InvalidOperationException("Aucune exécution en cours / No active run.");
            await using var db = Db();
            var input = await db.PendingInputs.AsNoTracking().SingleAsync(x => x.Id == I(p, "id") && x.ChatId == id, ct);
            if (input.Images().Count > 0 && !active.Provider.SupportsImages && !VisionBridge.Enabled(await db.States.Select(x=>x.EnabledSkills).SingleAsync(ct))) throw new InvalidOperationException("Le modèle en cours n’accepte pas les images.");
            providerId = active.SelectedProviderId;
        }
        await ConversationInbox.UpdateAsync(database, id, I(p, "id"), S(p, "expectedText"), S(p, "text"), steer, providerId, ct);
        await NotifyInbox(id); return true;
    }
    async Task<object> AddInbox(JsonObject p,CancellationToken ct)
    {
        var id=I(p,"chatId");var mode=S(p,"mode","queued");var images=InputImages(p);
        runs.TryGetValue(id,out var active);
        await using var db=Db();var all=await db.Providers.ToListAsync(ct);
        var providerId=mode=="steering"&&active!=null?active.SelectedProviderId:I(p,"providerId");
        var provider=all.Single(x=>x.Id==providerId);
        if(provider.IsComposite){var composite=CompositeModel.Read(provider.CompositeJson);composite.Validate(all);provider=CompositeModel.Resolve(composite.Orchestrator,all);}
        if(images.Count>0 && !(mode=="steering"&&active!=null?active.Provider:provider).SupportsImages && !VisionBridge.Enabled(await db.States.Select(x=>x.EnabledSkills).SingleAsync(ct)))throw new InvalidOperationException("Ce modèle n’accepte pas les images.");
        await ConversationInbox.AddAsync(database,id,providerId,S(p,"text"),images,mode,ct);
        await NotifyInbox(id);return new{running=runs.ContainsKey(id),autoStart=active==null||!active.Cancellation.IsCancellationRequested};
    }
    async Task<object?> SendNext(int id,CancellationToken ct)
    {
        if(runs.ContainsKey(id))return false;
        await using var db=Db();
        var item=await db.PendingInputs.Where(x=>x.ChatId==id).OrderByDescending(x=>x.Mode=="steering").ThenBy(x=>x.Id).FirstOrDefaultAsync(ct);
        if(item==null)return false;
        return await Send(new(){["chatId"]=id,["providerId"]=item.ProviderId,["text"]=item.Text,["images"]=System.Text.Json.JsonSerializer.SerializeToNode(item.Images(),Json),["pendingInputId"]=item.Id},ct);
    }
    async Task<List<Message>> ApplySteering(ConversationSession run,CancellationToken ct)
    {
        var added=await ConversationInbox.ApplySteeringAsync(run,ct);
        foreach(var item in added)await emit(new{@event="message",chatId=run.Chat.Id,message=MessageView(item)});
        if(added.Count>0)await NotifyInbox(run.Chat.Id);return added;
    }
}
