using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;

static class SchedulingChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var a=new Provider{Id=10,Name="OpenAI",Model="a"};var b=new Provider{Id=11,Name="OpenAI",Model="b"};
        ProviderModels.Refresh(a,["a","new","a"]);ProviderModels.Select(a,["new"]);
        check(ProviderModels.Visible(a).SequenceEqual(["new"]) && ProviderModels.Visible(b).SequenceEqual(["b"]),"Modèles cochés isolés par connexion, noms identiques");
        ProviderModels.Refresh(a,["new","latest"]);check(ProviderModels.Available(a).Count==2 && ProviderModels.Visible(a).SequenceEqual(["new"]),"Refresh remplace les modèles détectés et conserve les choix");
        ProviderModels.Select(a,[]);check(ProviderModels.Visible(a).Count==0,"Tout décocher ne réactive pas le modèle précédent");
        ProviderModels.Refresh(a,["latest"]);check(ProviderModels.Visible(a).Count==0,"Refresh ne recoche pas de modèle automatiquement");
        var utc=new DateTime(2026,9,22,12,1,0,DateTimeKind.Utc);
        var task=new ScheduledTask{Cron="*/15 * * * *",TimeZoneId="UTC",ProviderId=1,Model="scheduled-model",Instruction="Test"};
        check(task.Next(utc)==new DateTime(2026,9,22,12,15,0,DateTimeKind.Utc),"CRON toutes les quinze minutes");
        task.Cron="30 9 * * 1";check(task.Next(utc)==new DateTime(2026,9,28,9,30,0,DateTimeKind.Utc),"CRON hebdomadaire");
        task.Cron="0 9 * * *";task.TimeZoneId="Europe/Paris";
        check(task.Next(utc)==new DateTime(2026,9,23,7,0,0,DateTimeKind.Utc),"CRON applique le fuseau Europe/Paris");
        check(task.Next(new DateTime(2026,10,25,0,0,0,DateTimeKind.Utc))==new DateTime(2026,10,25,8,0,0,DateTimeKind.Utc),"CRON respecte le changement d’heure d’hiver");
        bool invalid=false;try { task.Cron="invalid";task.Validate(); }catch { invalid=true; }check(invalid,"CRON invalide refusé");
        var folder=Path.Combine(Path.GetTempPath(),"omh-schedule-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var path=Path.Combine(folder,"database.sqlite");
        using var db=new HarnessDb(path);await db.InitializeAsync();
        task.ProjectId=(await db.Projects.FirstAsync()).Id;task.ProviderId=(await db.Providers.FirstAsync()).Id;
        task.Cron="* * * * *";task.TimeZoneId="UTC";task.NextRunUtc=utc.AddDays(-1);task.Enabled=true;task.ThinkingLevel="high";task.ResourcePathsJson="[]";
        db.ScheduledTasks.Add(task);await db.SaveChangesAsync();
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int runs=0;
        using var scheduler=new TaskSchedulerService(path,async(_,ct)=>{Interlocked.Increment(ref runs);await gate.Task.WaitAsync(ct);return "completed";});
        await scheduler.TickAsync(utc);await scheduler.TickAsync(utc.AddMinutes(3));
        check(runs==1,"Une tâche ne chevauche pas sa propre exécution");
        using(var second=new TaskSchedulerService(path,(_,_)=>{Interlocked.Increment(ref runs);return Task.FromResult("duplicate");}))await second.TickAsync(utc.AddMinutes(4));
        check(runs==1,"Deux instances ne lancent pas la même tâche en parallèle");
        var stored=await db.ScheduledTasks.AsNoTracking().SingleAsync();check(stored.NextRunUtc==utc.AddMinutes(1),"Les échéances manquées sont regroupées, sans boucle de rattrapage");
        gate.SetResult();
        for(int i=0;i<100 && (await db.ScheduledTasks.AsNoTracking().SingleAsync()).LastResult!="completed";i++)await Task.Delay(20);
        check((await db.ScheduledTasks.AsNoTracking().SingleAsync()).LastResult=="completed","Résultat de la tâche persisté");
        var first=await ScheduledTaskInput.PrepareAsync(path,task,_=>false,default);
        check(first.Options.ThinkingLevel=="high" && first.Chat.ResourcePathsJson=="[]" && first.Provider.Model=="scheduled-model","Réglages capturés pour l’exécution sans modifier le fournisseur");
        check((await db.Providers.AsNoTracking().FirstAsync()).Model!="scheduled-model","Le modèle propre à la tâche ne modifie pas le modèle global");
        task.LastChatId=first.Chat.Id;task.RememberHistory=true;
        using(var messages=new HarnessDb(path)){messages.Messages.Add(new Message{ChatId=first.Chat.Id,Content="mémoire"});await messages.SaveChangesAsync();}
        var remembered=await ScheduledTaskInput.PrepareAsync(path,task,_=>false,default);
        check(remembered.Chat.Id==first.Chat.Id,"Mode mémoire réutilise la conversation");
        bool busy=false;try { await ScheduledTaskInput.PrepareAsync(path,task,_=>true,default); }catch(InvalidOperationException){busy=true;}check(busy,"Tâche mémoire ne reprend pas une conversation occupée");
        task.RememberHistory=false;var fresh=await ScheduledTaskInput.PrepareAsync(path,task,_=>false,default);
        check(fresh.Chat.Id!=first.Chat.Id && !await db.Messages.AnyAsync(x=>x.ChatId==fresh.Chat.Id),"Sans mémoire, nouvelle conversation vide à chaque exécution");
        check(await db.Messages.AnyAsync(x=>x.ChatId==first.Chat.Id),"L’historique des anciennes exécutions reste consultable");
        await db.ScheduledTasks.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enabled,false));await scheduler.TickAsync(utc.AddHours(1));check(runs==1,"Une tâche désactivée ne démarre plus");
        // Tests use an isolated directory; keep it until pooled SQLite connections are released by process exit.
    }
}
