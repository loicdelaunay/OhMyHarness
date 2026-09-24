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
        new("mist", "Brume", "Mist", false, "#EDF2F8", "#FAFCFF", "#1F2F43", "#57697D", "#245CB2"),
        // Airbus Blue #00205B, paired with near-black or white neutral surfaces.
        // https://www.brand.airbus.com/en/asset-library/airbus-logo
        new("fly-dark", "Fly dark", "Fly dark", true, "#090D14", "#102746", "#FFFFFF", "#ADB5C1", "#00205B"),
        new("fly-light", "Fly light", "Fly light", false, "#FFFFFF", "#F5F5F5", "#00205B", "#596474", "#00205B"),
        new("electric-dark", "Electric sombre", "Electric dark", true, "#0E0F12", "#191D24", "#F2F8FA", "#AFBEC6", "#4CC9F0"),
        new("electric-light", "Electric clair", "Electric light", false, "#F0FAFD", "#FFFFFF", "#0E0F12", "#52636C", "#4CC9F0")
    ];
    public static AppearanceTheme Get(string? id) => All.FirstOrDefault(x => x.Id == id) ?? All[0];
}
