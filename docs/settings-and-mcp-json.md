# Settings, themes and MCP.json

General offers themes persisted in the existing SQLite settings field: Fluent dark, Midnight, Forest, Fluent light, Ivory, Mist, Fly dark and Fly light, plus Electric dark/light. The displayed name and logo are customizable in the same section; see [portable logo storage](branding.md). Input shortcuts remain active, but their old label has been removed from Settings.

Providers appear as cards. The gear opens the selected connection's form; adding, duplicating, deleting and composite models remain available.

Model information, speed, context and input share one block. Its header button collapses or expands the information and saves that choice in SQLite.

## Response style

**General → Response style** and the CLI's **/settings → Response style** share a saved preference:

| Style | Behavior |
| --- | --- |
| DEFAULT | No additional style instruction; existing behavior is unchanged. |
| SHORT (COURT) | The minimum answer needed to satisfy the request. |
| PRAGMATIC (PRAGMATIQUE) | Fairly short, practical answers focused on results and next steps. |
| DETAILED (DÉTAILLÉ) | Detailed explanations with useful context and examples. |
| FUN (AMUSANT) | A playful, friendly tone with appropriate light humor. |

The setting applies to subsequent sends in both interfaces, including OpenCode. It affects presentation, not task scope, permissions or required output formats. An already running response keeps the settings captured when it started.

## Portable MCP configuration

`MCP.json` is created in the application's portable folder, beside `database.sqlite` and the Windows EXE, through `PortableStorage.Root` (never in the standalone binary's extraction cache). The macOS host supplies its portable folder to the service.

In MCP Settings, **Edit MCP.json** opens the editor. Accepted example:

```json
{
  "mcpServers": {
    "godot": {
      "command": "npx",
      "args": ["@coding-solo/godot-mcp"],
      "env": {
        "GODOT_PATH": "/path/to/godot",
        "DEBUG": "true"
      }
    }
  }
}
```

Replace the path with your Godot path. Saving does not launch a connection; connections and calls use existing MCP permissions. Servers are enabled by default in this format; `"enabled": false` disables them. For HTTP/SSE, `url`, `transport` and `headers` are also accepted. The optional working directory is `cwd`.

The file is validated before applying it to SQLite, which retains the identifiers and protected secrets needed by the engine. Manual changes load at startup, when viewing configuration and when loading tools. Invalid JSON leaves previous servers available. An external change made during editing requires reloading rather than overwriting it.

Secrets already stored in SQLite are never exported in clear text. Explicit `env`/`headers` values in JSON naturally remain in that file and are encrypted in SQLite. If a secret is later replaced through the form, its old explicit value is removed from the file. Internal comparison of unchanged secrets preserves permission fingerprints. `MCP.json` is excluded from Git.

Server names must be unique. Form additions, deletions and toggles are reflected in the file. If the form and file are changed simultaneously, reload the file before saving.

## Validation

- .NET checks for persistence, Godot-format parsing, absence of secret exports, synchronization and file conflicts.
- Service tests for JSON APIs, the original eight themes and toggle synchronization.
- Electron smoke test: light theme, provider cards, JSON editor and composer collapse.
- WinUI Windows build and publish; native visual validation was interrupted by the user (Escape). No macOS visual check was performed.
