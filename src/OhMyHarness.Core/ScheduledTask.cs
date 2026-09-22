using Cronos;
using Microsoft.EntityFrameworkCore;

namespace OhMyHarness.Core;

public sealed class ScheduledTask
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; } = "Nouvelle tâche";
    public string Instruction { get; set; } = "";
    public string Cron { get; set; } = "0 * * * *";
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public bool Enabled { get; set; } = true;
    public bool RememberHistory { get; set; }
    public int? LastChatId { get; set; }
    public int ProviderId { get; set; }
    public string Model { get; set; } = "";
    public int ContextLimit { get; set; } = 128000;
    public bool SupportsImages { get; set; }
    public string ThinkingLevel { get; set; } = "auto";
    public string ResourcePathsJson { get; set; } = "";
    public string ExecutionMode { get; set; } = "execute";
    public string OrchestrationMode { get; set; } = "disabled";
    public bool SandboxEnabled { get; set; }
    public string EnabledSkills { get; set; } = "sources,web";
    public bool AutoContinue { get; set; }
    public DateTime? NextRunUtc { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public string LastResult { get; set; } = "";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Instruction) || string.IsNullOrWhiteSpace(Model))
            throw new ArgumentException("Nom, instruction et modèle requis. / Name, instruction and model required.");
        if (ContextLimit < 1024 || ProviderId <= 0) throw new ArgumentException("Fournisseur ou contexte invalide.");
        if (Next(DateTime.UtcNow) == null) throw new ArgumentException("Ce CRON ne comporte aucune prochaine exécution. / No future occurrence for this CRON.");
    }
    public DateTime? Next(DateTime afterUtc) => CronExpression.Parse(Cron).GetNextOccurrence(
        DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId));
}

/// <summary>Persists the next occurrence before dispatch; never overlaps one task or replays a backlog.</summary>
public sealed class TaskSchedulerService(string database, Func<ScheduledTask, CancellationToken, Task<string>> execute) : IDisposable
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<int, byte> running = new();
    readonly SemaphoreSlim tickGate = new(1, 1);
    readonly CancellationTokenSource lifetime = new();
    FileStream? lease;
    public async Task TickAsync(DateTime nowUtc)
    {
        if (!await tickGate.WaitAsync(0)) return;
        try
        {
            bool acquired = false;
            if (lease == null)
            {
                try { lease = new FileStream(database + ".scheduler.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); acquired = true; }
                catch (IOException) { return; } // Another application instance owns the scheduler.
            }
            using var db = new HarnessDb(database);
            if (acquired) await db.ScheduledTasks.Where(x => x.LastResult == "En cours / Running")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastResult, "Interrompue à la fermeture / Interrupted on exit"), lifetime.Token);
            var candidates = await db.ScheduledTasks.AsNoTracking().Where(x => x.Enabled).ToListAsync(lifetime.Token);
            foreach (var task in candidates)
            {
                if (running.ContainsKey(task.Id)) continue;
                if (task.NextRunUtc == null)
                {
                    var next = task.Next(nowUtc);
                    await db.ScheduledTasks.Where(x => x.Id == task.Id && x.NextRunUtc == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextRunUtc, next), lifetime.Token);
                    continue;
                }
                if (task.NextRunUtc > nowUtc) continue;
                var nextRun = task.Next(nowUtc);
                // Conditional update prevents two application instances from claiming the same occurrence.
                var claimed = await db.ScheduledTasks.Where(x => x.Id == task.Id && x.Enabled && x.NextRunUtc == task.NextRunUtc)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextRunUtc, nextRun)
                        .SetProperty(x => x.LastRunUtc, nowUtc).SetProperty(x => x.LastResult, "En cours / Running"), lifetime.Token);
                if (claimed == 0) continue;
                running.TryAdd(task.Id, 0);
                _ = ExecuteAsync(task);
            }
        }
        finally { tickGate.Release(); }
    }
    async Task ExecuteAsync(ScheduledTask task)
    {
        string result;
        try { result = await execute(task, lifetime.Token); }
        catch (OperationCanceledException) { result = "Arrêtée / Stopped"; }
        catch (Exception ex) { result = "Erreur / Error: " + ex.Message; }
        try
        {
            using var db = new HarnessDb(database);
            await db.ScheduledTasks.Where(x => x.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastResult, result));
        }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError("Scheduled task persistence: " + ex.Message); }
        finally { running.TryRemove(task.Id, out _); }
    }
    public void Dispose() { lifetime.Cancel(); lease?.Dispose(); }
}
