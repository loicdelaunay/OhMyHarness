namespace OhMyHarness.Core;

public static class ModelCatalog
{
    public static List<string> GetModelsForProvider(Provider? provider) => provider == null ? [] :
        ProviderModels.Normalize(ProviderModels.Available(provider).Concat(ProviderModels.Visible(provider)));

    public static int? GetDefaultContextLimit(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;

        var m = model.ToLowerInvariant();
        if (m.Contains("muse-spark")) return 1_048_576;
        if (m.Contains("big-pickle")) return 200_000;
        if (m.Contains("nemotron")) return 262_144;
        if (m.Contains("mimo")) return 200_000;
        if (m.Contains("ling-3")) return 262_144;
        if (m.Contains("ox-alpha")) return 128_000;
        if (m.Contains("o1") || m.Contains("o3")) return 200_000;
        if (m.Contains("deepseek-reasoner")) return 64_000;
        if (m.Contains("deepseek")) return 128_000;
        if (m.Contains("gpt-4o") || m.Contains("gpt-4.1")) return 128_000;
        if (m.Contains("llama3")) return 128_000;
        if (m.Contains("qwen2.5")) return 128_000;
        if (m.Contains("gpt-3.5")) return 16_384;
        return null;
    }
}
