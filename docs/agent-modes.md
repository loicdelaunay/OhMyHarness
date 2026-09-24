# Modes, subagents and custom skills

The **+** menu provides two per-conversation settings, saved in SQLite and captured when the next message is sent. Changing a setting does not affect an active generation; stop it before restarting in another mode.

## Plan and Execution

**Execution** keeps enabled tools and their usual permissions.

**Plan** goes beyond an instruction to the model: prohibited tools are removed from their definitions and calls are rejected before execution, even if the provider sends an unexpected call or permissions are set to automatic approval. Source reads, glob/grep, Git, inspection of an already open page and screenshots remain available according to enabled skills. Writing, patching, terminal commands (including supposedly read-only commands), navigation, keyboard, mouse and MCP are blocked. Commands entered manually in the Terminal tab remain user actions.

With OpenCode, the message carries a global tool-deny rule followed only by exceptions for native read tools `read`, `glob`, `grep`, `list` when OpenCode tools are enabled. Remaining permission requests are denied in Plan mode. Hooks/plugins run by an external OpenCode installation remain under its control; this mode does not sandbox the external process.

## Orchestration

- **Disable**: no delegation tool in the direct engine; forged calls are rejected. OpenCode's native `task` tool is disabled.
- **Auto**: the model decides when to call `delegate_tasks` for independent tasks. In OpenCode Execution mode, its native `task` tool may be used if OpenCode tools are enabled.
- **Forced**: before the main answer, two read-only subagents examine sources/conventions and risks/validation criteria respectively. Their reports appear in the conversation and are retained. The parent can then delegate further tasks with the direct engine.

Direct subagents use the conversation's provider and model, with separate context, project instructions and enabled skills. They can read or modify sources according to the inherited mode; forced analyses remain in Plan mode. They have no terminal, desktop, browser or MCP tools and cannot delegate recursively. Limits: three tasks per call, six subagents per message and eight steps per subagent. Reports distinguish limits reached from completed answers. Stopping a conversation also cancels its subagents. Their calls consume additional tokens.

Forced OpenCode consultations use separate sessions and native read tools. Additional native OpenCode subagents are managed by OpenCode; they do not use the local `delegate_tasks` loop or its limits.

## Project instructions

**AGENTS.md** files in attached roots and subfolders are loaded at the start of each message. Excluded paths and symbolic links are skipped or rejected. Each text is accompanied by its scope directory; subfolder rules take precedence for files in that subfolder. Files outside attached roots are not scanned.

The original loading limits were 32 KB per file, 64 KB overall and 2,000 directories per root, with an error when a limit was reached. Current loading is asynchronous and best effort: see [the guide](guide.md) for updated behavior. Project conventions cannot change permissions or Plan mode.

## Skills folder

Native Windows: `skills/` beside the executable. In the Electron host, it sits beside `database.sqlite` (beside the distributed application; `.data` in development). The exact path appears under **Settings → Skills**.

An example `skills/exemple-revue/SKILL.md` and its `resources/checklist.md` resource are supplied without replacing your customizations. To import a skill, copy its folder into `skills`, then reopen Settings or **+ → Skills**. Enable the desired skill: new imported skills are disabled by default.

Each folder must contain a `SKILL.md` with this minimal frontmatter (single-line values):

```markdown
---
name: my-skill
description: When and why to use this skill.
---
# Instructions
Describe the steps and validation criteria here.
```

The name must match the folder (lowercase letters, digits and hyphens). Only enabled skill descriptions are sent initially. The model loads content with `load_skill`, then text resources with `read_skill_resource`. No script runs automatically. Resources stay within the skill folder. OpenCode can read the explicit `SKILL.md` path with its native tool; it retains its own tool and permission management.
