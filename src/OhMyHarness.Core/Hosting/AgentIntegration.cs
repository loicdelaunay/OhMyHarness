using OhMyHarness.Core;
using Microsoft.EntityFrameworkCore;

namespace OhMyHarness.Core.Hosting;

public sealed partial class HarnessService
{
    AgentRuntime CreateAgentRuntime(ConversationSession run, string secret) => new(run, new CustomSkills(CustomSkills.DefaultRoot, run.Project.GetSourceFolders(), run.Project.Id),
        async (wire, definitions, ct) =>
        {
            if (!run.Provider.IsOpenCode) return await new ChatEngine(http).StreamAsync(run.Provider, secret, wire, definitions, _ => { }, ct, run.Options.ThinkingLevel);
            var directory = OpenCodeDirectory(run.Project);
            await EnsureOpenCode(run.Provider, secret, directory, ct);
            var engine = new OpenCodeEngine(http);
            var session = await engine.CreateSessionAsync(run.Provider, secret, directory, "Sous-agent · " + run.Chat.Title, ct);
            return await engine.PromptAsync(run.Provider, secret, directory, session, wire.Last()?["content"]?.GetValue<string>() ?? "",
                wire[0]?["content"]?.GetValue<string>() ?? "", [], _ => { }, ct, policy: new("plan", "disabled"), workflow: run.Workflow?.ForChild());
        },
        (scope, diff, ct) => Approve(scope, run.Chat.Title + (scope.StartsWith("memory|") ? " · Mémoire / Memory" : " · Sous-agent · Patch"), diff, ct),
        text => emit(new { @event = "status", chatId = run.Chat.Id, text }),
        async ct => { await using var db = Db(); return await db.States.Select(x => x.EnabledSkills).SingleAsync(ct); },
        child => emit(new { @event="subagent", chatId=run.Chat.Id, child }),
        async (target,wire,definitions,ct)=> {
            var key=await Decrypt(target.ProtectedKey,ct);
            if(!target.IsOpenCode)return await new ChatEngine(http).StreamAsync(target,key,wire,definitions,_=>{},ct,run.Options.ThinkingLevel);
            var directory=OpenCodeDirectory(run.Project);await EnsureOpenCode(target,key,directory,ct);
            var engine=new OpenCodeEngine(http);var id=await engine.CreateSessionAsync(target,key,directory,"Sous-agent · "+run.Chat.Title,ct);
            return await engine.PromptAsync(target,key,directory,id,wire.Last()?["content"]?.GetValue<string>()??"",wire[0]?["content"]?.GetValue<string>()??"",[],_=>{},ct,policy:new("plan","disabled"),workflow:run.Workflow?.ForChild());
        }, async (id, ct) => {
            await using var db = Db();
            var state = await db.States.SingleAsync(ct);
            if (!Skills.Enabled(state.EnabledSkills, id)) state.EnabledSkills += "," + id;
            await db.SaveChangesAsync(ct);
        });
}
