namespace OhMyHarness.Core.Hosting;

/// <summary>Host capabilities apply only to this process; shared settings are not rewritten.</summary>
public sealed record HarnessServiceOptions
{
    public string? PermissionModeOverride { get; init; }
    public bool UseNativeKeyVault { get; init; }
    public bool SupportsLocalPreview { get; init; } = true;
    public IReadOnlySet<string> DisabledSkills { get; init; } = new HashSet<string>();

    internal void Apply(AppState state) => state.EnabledSkills = string.Join(',',
        state.EnabledSkills.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(id => !DisabledSkills.Contains(id)));
}
