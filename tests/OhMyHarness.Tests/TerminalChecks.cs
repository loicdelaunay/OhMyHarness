using OhMyHarness.Core;

static class TerminalChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        using var hub = new TerminalHub();
        var root = Path.GetTempPath();
        var a = hub.Create(1, false, "Build", root); var b = hub.Create(1, false, "Tests", root);
        var enteredA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = hub.Start(1, false, a.Id, "a", async (_, output, ct) => { output("A running"); enteredA.SetResult(); await release.Task.WaitAsync(ct); return "Exit code: 0\nA done"; }, default);
        var second = hub.Start(1, false, b.Id, "b", async (_, output, ct) => { output("B running"); enteredB.SetResult(); await release.Task.WaitAsync(ct); return "Exit code: 0\nB done"; }, default);
        await Task.WhenAll(enteredA.Task, enteredB.Task).WaitAsync(TimeSpan.FromSeconds(5));
        check(hub.List(1).Count(x => x.Status == "running") == 2, "Deux terminaux exécutent réellement en parallèle");
        check(hub.Read(1, false, a.Id).Output == "A running", "Sortie terminal disponible avant la fin");
        check((await hub.WaitAsync(1, false, a.Id, first.JobId, 10, default)).Status == "running", "Attente bornée rend la main sans arrêter le processus");
        bool rejected = false;
        try { hub.Read(2, false, a.Id); } catch (UnauthorizedAccessException) { rejected = true; }
        check(rejected, "Terminal d'une autre conversation inaccessible");
        rejected = false; try { hub.Read(1, true, a.Id); } catch (UnauthorizedAccessException) { rejected = true; }
        check(rejected, "Terminal local inaccessible depuis le mode sandbox");
        rejected = false; try { hub.Start(1, false, a.Id, "duplicate", (_, _, _) => Task.FromResult("bad"), default); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "Deux commandes dans le même terminal refusées");
        await hub.DeleteAsync(1, false, a.Id);
        check(hub.List(1).Count == 1 && hub.Read(1, false, b.Id).Status == "running", "Fermer un terminal arrête uniquement son travail");
        release.SetResult();
        var done = await hub.WaitAsync(1, false, b.Id, second.JobId, 5000, default);
        check(done.Status == "completed" && done.Output.Contains("B done"), "Résultat final récupéré par identifiant de job");
        var next = hub.Start(1, false, b.Id, "next", (_, _, _) => Task.FromResult("Exit code: 0\nnext"), default);
        await hub.WaitAsync(1, false, b.Id, next.JobId, 5000, default);
        check(hub.Read(1, false, b.Id, second.JobId).Output.Contains("B done"), "Ancien résultat conservé lors de la réutilisation d'un onglet");
        check(AgentPolicy.Allowed("plan", "list_terminals") && AgentPolicy.Allowed("plan", "wait_terminal") && !AgentPolicy.Allowed("plan", "start_terminal"), "Plan permet l'observation mais bloque le lancement");
        check(TerminalHub.IsBoundedWait("wait_terminal", "{}") && !TerminalHub.IsBoundedWait("wait_terminal", "{\"timeout_ms\":0}"), "Attentes bornées distinguées des boucles d'interrogation immédiate");
        var actual = hub.Create(3, false, "Shell", root);
        var cmd = OperatingSystem.IsWindows() ? "Write-Output 'started'; Start-Sleep -Seconds 20; Write-Output 'should-not-finish'" : "printf 'started\\n'; sleep 20; printf 'should-not-finish\\n'";
        var started = hub.Start(3, false, actual.Id, cmd, (command, output, ct) => WorkspaceTools.ShellAsync(command, root, ct, output), default);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!hub.Read(3, false, actual.Id).Output.Contains("started") && DateTime.UtcNow < until) await Task.Delay(50);
        check(hub.Read(3, false, actual.Id).Output.Contains("started"), "Sortie progressive d'un vrai processus shell");
        await hub.StopChatAsync(3);
        var stopped = hub.Read(3, false, actual.Id, started.JobId);
        check(stopped.Status == "cancelled" && !stopped.Output.Contains("should-not-finish"), "Annulation du processus shell sans attendre sa fin normale");
    }
}
