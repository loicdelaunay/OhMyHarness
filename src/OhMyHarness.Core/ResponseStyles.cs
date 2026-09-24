namespace OhMyHarness.Core;

public sealed record ResponseStyle(string Id, string French, string English, string Instruction);

public static class ResponseStyles
{
    public static IReadOnlyList<ResponseStyle> All { get; } = new ResponseStyle[]
    {
        new("default", "DEFAULT", "DEFAULT", ""),
        new("short", "COURT", "SHORT", "Answer with the minimum text needed to satisfy the request. Omit introductions, repetition and optional explanations."),
        new("pragmatic", "PRAGMATIQUE", "PRAGMATIC", "Keep answers fairly short and practical. Lead with the result, concrete actions or next steps. Include only explanations that help the user act or decide."),
        new("detailed", "DÉTAILLÉ", "DETAILED", "Provide detailed, well-structured explanations. Include useful context, reasoning summaries, examples and relevant caveats, without needless repetition."),
        new("fun", "AMUSANT", "FUN", "Use a playful, friendly tone with light humor when appropriate. Keep the answer useful and avoid jokes in serious or sensitive situations.")
    };
    public static ResponseStyle Get(string? id) => All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    public static string Prompt(string? id)
    {
        var style = Get(id);
        return style.Instruction.Length == 0 ? "" : "\nRESPONSE STYLE: " + style.Instruction
            + " This is a presentation preference for user-facing answers, not a limit on task completion. Respect explicit user requirements, required output formats, accuracy and tool permissions.\n";
    }
}
