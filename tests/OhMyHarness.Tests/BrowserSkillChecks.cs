using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;

static class BrowserSkillChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        check(Skills.All.Count(skill => skill.Id is BrowserSkillAccess.Access or BrowserSkillAccess.Dom) == 2,
            "Browser access and DOM access are ordinary built-in skills");
        string path = Path.Combine(Path.GetTempPath(), "omh-browser-skills-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            await using (var db = new HarnessDb(path))
            {
                await db.InitializeAsync();
                var state = await db.States.SingleAsync();
                state.EnabledSkills = BrowserSkillAccess.Set(state.EnabledSkills, true, true);
                await db.SaveChangesAsync();
            }
            await using (var db = new HarnessDb(path))
            {
                var stored = (await db.States.AsNoTracking().SingleAsync()).EnabledSkills;
                check(BrowserSkillAccess.Enabled(stored) && BrowserSkillAccess.DomEnabled(stored) && Skills.Enabled(stored, "web"),
                    "Browser and DOM choices survive reopening the application database");
                var disabled = BrowserSkillAccess.Set(stored, false, true);
                check(!BrowserSkillAccess.Enabled(disabled) && !BrowserSkillAccess.DomEnabled(disabled) && Skills.Enabled(disabled, BrowserSkillAccess.Dom) && Skills.Enabled(disabled, "web"),
                    "Disabling browser access blocks DOM without erasing other skill choices");
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }
}
