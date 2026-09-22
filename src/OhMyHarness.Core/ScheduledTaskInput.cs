using Microsoft.EntityFrameworkCore;

namespace OhMyHarness.Core;

public sealed record ScheduledTaskInput(Chat Chat, Project Project, Provider Provider, AppState Options, List<Provider> Providers)
{
    public static async Task<ScheduledTaskInput> PrepareAsync(string database, ScheduledTask task, Func<int,bool> isBusy, CancellationToken ct)
    {
        using var db=new HarnessDb(database);
        var project=await db.Projects.SingleAsync(x=>x.Id==task.ProjectId,ct);
        var providers=await db.Providers.AsNoTracking().ToListAsync(ct);
        var provider=providers.FirstOrDefault(x=>x.Id==task.ProviderId) ?? throw new InvalidOperationException("Le fournisseur de cette tâche a été supprimé.");
        provider.Model=task.Model;provider.ContextLimit=task.ContextLimit;provider.SupportsImages=task.SupportsImages;
        var options=await db.States.AsNoTracking().SingleAsync(ct);
        options.ThinkingLevel=task.ThinkingLevel;options.EnabledSkills=task.EnabledSkills;options.AutoContinue=task.AutoContinue;
        var chat=task.RememberHistory && task.LastChatId.HasValue ? await db.Chats.FirstOrDefaultAsync(x=>x.Id==task.LastChatId && x.ProjectId==task.ProjectId,ct) : null;
        if(chat!=null && isBusy(chat.Id))throw new InvalidOperationException("Conversation déjà en cours : cette échéance est ignorée. / Conversation busy; occurrence skipped.");
        if(chat==null) { chat=new Chat{ProjectId=task.ProjectId,Title="◷ "+task.Name+" · "+DateTime.Now.ToString("g")};db.Chats.Add(chat); }
        chat.ResourcePathsJson=task.ResourcePathsJson;chat.ExecutionMode=task.ExecutionMode;chat.OrchestrationMode=task.OrchestrationMode;chat.SandboxEnabled=task.SandboxEnabled;
        await db.SaveChangesAsync(ct);
        await db.ScheduledTasks.Where(x=>x.Id==task.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LastChatId,chat.Id),ct);
        return new(chat,project,provider,options,providers);
    }
}
