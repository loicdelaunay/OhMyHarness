using OhMyHarness.Core;
using Microsoft.EntityFrameworkCore;

static class InboxChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var folder=Path.Combine(Path.GetTempPath(),"omh-inbox-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var file=Path.Combine(folder,"database.sqlite");
        try
        {
            await using var db=new HarnessDb(file);await db.InitializeAsync();
            var chat=await db.Chats.FirstAsync();var project=await db.Projects.FirstAsync();var provider=await db.Providers.FirstAsync();var options=await db.States.SingleAsync();
            var other=new Chat{ProjectId=project.Id};db.Chats.Add(other);await db.SaveChangesAsync();
            await ConversationInbox.AddAsync(file,chat.Id,provider.Id,"next",[],"queued");
            await ConversationInbox.AddAsync(file,chat.Id,provider.Id,"steer",[new(){Name="test.png",Mime="image/png",Data=[1,2,3]}],"steering");
            await ConversationInbox.AddAsync(file,other.Id,provider.Id,"other",[],"steering");
            await using(var reopened=new HarnessDb(file))check(await reopened.PendingInputs.CountAsync()==3,"File d’attente et consignes persistées après réouverture SQLite");
            using var run=new ConversationSession(chat,project,provider,options,"",[],file);
            var added=await ConversationInbox.ApplySteeringAsync(run,default);
            check(added.Count==1 && added[0].Content=="steer" && added[0].Attachments[0].Data.SequenceEqual(new byte[]{1,2,3}),"Consigne et image transférées à la conversation en cours");
            check(await db.PendingInputs.CountAsync()==2 && (await ConversationInbox.ApplySteeringAsync(run,default)).Count==0,"Consigne consommée une fois, file et autre conversation préservées");
            var queued=await db.PendingInputs.SingleAsync(x=>x.ChatId==chat.Id);run.PendingInputId=queued.Id;
            await ConversationInbox.ConsumeAsync(run,default);
            check(await db.PendingInputs.AnyAsync(x=>x.Id==queued.Id),"Envoi en attente conservé jusqu’à la sauvegarde du message utilisateur");
            run.Db.Messages.Add(new(){ChatId=chat.Id,Content=queued.Text});await run.Db.SaveChangesAsync();
            check(!await db.PendingInputs.AnyAsync(x=>x.Id==queued.Id),"Suppression atomique du message en attente lors de son envoi");
            var composed=new Provider{Id=999,Kind="composite",CompositeJson=new CompositeModel{Orchestrator=new(){ProviderId=provider.Id,Model="orchestrator"},Agents=[new(){ProviderId=provider.Id,Model="reviewer",Name="Review",Task="Inspect"}]}.Json()};
            using var team=new ConversationSession(chat,project,composed,options,"Inspect",[],file,[provider]);
            provider.Model="changed after capture";
            check(team.Provider.Model=="orchestrator" && team.AgentProviders["Review"].Model=="reviewer" && team.SelectedProviderId==999,"Modèles composés figent le routage de l’orchestrateur et du sous-agent");
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
    }
}
