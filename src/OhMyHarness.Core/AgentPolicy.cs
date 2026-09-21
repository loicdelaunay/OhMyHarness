using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public static class AgentPolicy
{
    public static string Mode(string? value) => value == "plan" ? "plan" : "execute";
    public static string Orchestration(string? value) => value is "auto" or "forced" ? value : "disabled";
    public static bool ReadOnly(string mode) => Mode(mode) == "plan";
    // An allow-list is deliberate: new tools, MCP and arbitrary shell commands cannot silently bypass Plan.
    public static bool Allowed(string mode, string tool) => !ReadOnly(mode) || tool is
        "rag_search" or "rag_sources" or "rag_read" or "list_sources" or "read_source" or "glob_sources" or "grep_sources" or "git_changes" or
        "read_page" or "inspect_dom" or "keyboard_keys" or "desktop_screens" or "desktop_screenshot" or "browser_screenshot" or
        "load_skill" or "read_skill_resource" or "delegate_tasks" or "todowrite" or "question" or "list_terminals" or "read_terminal" or "wait_terminal";
    public static void Demand(string mode, string tool)
    {
        if (!Allowed(mode, tool)) throw new UnauthorizedAccessException($"Mode Plan : outil '{tool}' interdit. Passez en Exécution au prochain envoi / Plan mode forbids this tool.");
    }
    public static void Filter(JsonArray definitions, string mode)
    {
        for (var i = definitions.Count - 1; i >= 0; i--)
            if (!Allowed(mode, definitions[i]?["function"]?["name"]?.GetValue<string>() ?? "")) definitions.RemoveAt(i);
    }
    public static string Prompt(string mode, string orchestration) =>
        (ReadOnly(mode)
            ? "\nPLAN MODE: Analyze, inspect and propose a plan. All writes, shell commands, navigation/interaction and external MCP calls are technically disabled. Do not attempt alternate tools to bypass this. User must switch to Execution for implementation."
            : "\nEXECUTION MODE: Implement the user request within the enabled tools and permission rules.") +
        (Orchestration(orchestration) == "disabled" ? "\nDelegation disabled: do the work yourself." :
            "\nDelegate independent, bounded subtasks with delegate_tasks when useful. You remain responsible for reviewing and integrating their results. Do not delegate overlapping writes. Subagents cannot delegate recursively.");
}

public sealed record OpenCodeRunPolicy(string Mode, string Orchestration);
