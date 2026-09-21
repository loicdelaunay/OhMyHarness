namespace OhMyHarness.Core;

public sealed record AppearanceTheme(string Id, string French, string English, bool Dark, string Background, string Surface, string Text, string Muted, string Accent)
{
    public override string ToString() => French;
}

public static class AppearanceThemes
{
    public static IReadOnlyList<AppearanceTheme> All { get; } = [
        new("fluent-dark", "Fluent sombre", "Fluent dark", true, "#202020", "#2D2D2D", "#F3F3F3", "#B8B8B8", "#60CDFF"),
        new("midnight", "Minuit", "Midnight", true, "#131C2C", "#202E43", "#EFF5FF", "#B5C5DE", "#A7BCFF"),
        new("forest", "Forêt", "Forest", true, "#17231F", "#273830", "#EFF7F1", "#B5CABB", "#8EDDB0"),
        new("fluent-light", "Fluent clair", "Fluent light", false, "#F3F3F3", "#FFFFFF", "#202020", "#606060", "#0067A3"),
        new("ivory", "Ivoire", "Ivory", false, "#F5F0E7", "#FFFCF6", "#30281F", "#716252", "#94501A"),
        new("mist", "Brume", "Mist", false, "#EDF2F8", "#FAFCFF", "#1F2F43", "#57697D", "#245CB2")
    ];
    public static AppearanceTheme Get(string? id) => All.FirstOrDefault(x => x.Id == id) ?? All[0];
}
