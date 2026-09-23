using System.Text.Json;

namespace OhMyHarness.Core;

/// <summary>Catalogues belong to a connection, never to its display name or provider family.</summary>
public static class ProviderModels
{
    public static List<string> Parse(string json)
    {
        try { return Normalize(JsonSerializer.Deserialize<List<string>>(json) ?? []); }
        catch (JsonException) { return []; }
    }
    public static List<string> Normalize(IEnumerable<string> models) => models.Where(x => !string.IsNullOrWhiteSpace(x))
        .Select(x => x.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToList();
    public static List<string> Available(Provider provider) => provider.IsComposite ? [provider.Model] :
        Parse(provider.DetectedModelsJson);
    public static List<string> Visible(Provider provider) => provider.IsComposite ? [provider.Model] :
        string.IsNullOrWhiteSpace(provider.SelectedModelsJson) ? Normalize([provider.Model]) : Parse(provider.SelectedModelsJson);
    public static void Refresh(Provider provider, IEnumerable<string> detected)
    {
        var models = Normalize(detected);
        var available = models.ToHashSet(StringComparer.Ordinal);
        var selected = Visible(provider).Where(available.Contains).ToList();
        provider.DetectedModelsJson = JsonSerializer.Serialize(models);
        provider.SelectedModelsJson = JsonSerializer.Serialize(selected);
    }
    public static void Select(Provider provider, IEnumerable<string> selected) =>
        provider.SelectedModelsJson = JsonSerializer.Serialize(Normalize(selected));
}
