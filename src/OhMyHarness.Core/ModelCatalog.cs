namespace OhMyHarness.Core;

public static class ModelCatalog
{
    public static readonly string[] DeepSeekModels =
    [
        "deepseek-chat",
        "deepseek-reasoner",
        "deepseek-v3",
        "deepseek-v2.5",
        "deepseek-r1",
        "deepseek-r1:1.5b",
        "deepseek-r1:7b",
        "deepseek-r1:8b",
        "deepseek-r1:14b",
        "deepseek-r1:32b",
        "deepseek-r1:70b",
        "deepseek-coder-v2:16b",
        "deepseek-coder-v2:236b",
        "deepseek-coder:1.3b",
        "deepseek-coder:6.7b",
        "deepseek-coder:33b",
        "deepseek-llm:7b",
        "deepseek-llm:67b",
        "deepseek-flash",
        "deepseek/deepseek-r1",
        "deepseek/deepseek-chat",
        "deepseek/deepseek-r1-distill-llama-70b",
        "deepseek/deepseek-r1-distill-qwen-32b",
        "deepseek/deepseek-r1-distill-qwen-14b",
        "deepseek/deepseek-r1-distill-qwen-1.5b"
    ];

    public static readonly string[] OpenAiModels =
    [
        "gpt-4o",
        "gpt-4o-mini",
        "gpt-4.1-mini",
        "gpt-4.1",
        "o1",
        "o1-mini",
        "o3-mini",
        "gpt-4-turbo",
        "gpt-4",
        "gpt-3.5-turbo"
    ];

    public static readonly string[] GenericModels =
    [
        "gpt-4o",
        "gpt-4o-mini",
        "deepseek-chat",
        "deepseek-reasoner",
        "deepseek-r1:7b",
        "deepseek-r1:14b",
        "deepseek-r1:32b",
        "deepseek-r1:70b",
        "llama3.3:70b",
        "llama3.1:8b",
        "qwen2.5-coder:32b",
        "mistral-large"
    ];

    public static List<string> GetModelsForProvider(Provider? provider)
    {
        if (provider == null) return [.. DeepSeekModels];

        var name = provider.Name ?? "";
        var url = provider.BaseUrl ?? "";

        string[] baseCatalog;
        if (name.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("deepseek", StringComparison.OrdinalIgnoreCase))
        {
            baseCatalog = DeepSeekModels;
        }
        else if (name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("openai", StringComparison.OrdinalIgnoreCase))
        {
            baseCatalog = OpenAiModels;
        }
        else
        {
            baseCatalog = GenericModels;
        }

        var result = new List<string>(baseCatalog);
        if (!string.IsNullOrWhiteSpace(provider.Model) && !result.Contains(provider.Model, StringComparer.OrdinalIgnoreCase))
        {
            result.Insert(0, provider.Model);
        }

        return result;
    }

    public static int? GetDefaultContextLimit(string model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;

        var m = model.ToLowerInvariant();
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
