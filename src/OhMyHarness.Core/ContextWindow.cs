using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public static class ContextWindow
{
    public const double CompactThreshold = 0.95;
    public const double CompactTarget = 0.60;

    public static int EstimateText(string? text) => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, (int)Math.Ceiling(text.Length / 4d));

    public static int Estimate(JsonNode? node)
    {
        if (node == null) return 0;
        if (node is JsonArray array) return 2 + array.Sum(Estimate);
        if (node is JsonObject obj) return 2 + obj.Sum(x => EstimateText(x.Key) + Estimate(x.Value));
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
                return text.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ? 1000 : EstimateText(text);
            return 1;
        }
        return 1;
    }

    public static bool ShouldCompact(int estimatedTokens, int contextLimit) => contextLimit > 0 && estimatedTokens >= Math.Ceiling(contextLimit * CompactThreshold);
}
