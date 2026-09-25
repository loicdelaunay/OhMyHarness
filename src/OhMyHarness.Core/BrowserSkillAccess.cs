namespace OhMyHarness.Core;

/// <summary>Browser capabilities use the same persisted selection as the other built-in skills.</summary>
public static class BrowserSkillAccess
{
    public const string Access = "browser_access";
    public const string Dom = "browser_dom_access";

    public static bool Enabled(string selection) => Skills.Enabled(selection, Access);
    public static bool DomEnabled(string selection) => Enabled(selection) && Skills.Enabled(selection, Dom);

    public static string Set(string selection, bool access, bool dom)
    {
        var ids = selection.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (access) ids.Add(Access); else ids.Remove(Access);
        if (dom) ids.Add(Dom); else ids.Remove(Dom);
        return string.Join(',', ids);
    }
}
