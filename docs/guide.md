# OhMyHarness

**Tired of full-featured AI harnesses that require lengthy configuration, multiple services and tools to install before your first chat? What if one portable EXE did most of the work?**

OhMyHarness brings conversations, agents, sources and tools together in a desktop application. On Windows, it is distributed as a **standalone EXE**: place it in a writable folder, launch it and configure your provider. The application creates `database.sqlite` and its resources beside the executable. To move your workspace, close the application and copy the whole folder.

The interface uses **Uno Platform / .NET 10**. It targets Windows (Uno Desktop or native WinUI) and macOS (Uno Desktop). The [macOS workflow](macos.md) still needs validation on a real Mac. A separate [CLI](cli.md) shares the agent engine and portable data.

## What you can do

- **Connect your models**: multiple OpenAI v1-compatible, DeepSeek or OpenCode connections, each with its key, URL and selected models. You can also combine an orchestrator model with specialized subagents.
- **Work across conversations**: projects, shared source folders, simultaneous generations, queued or steered messages, resuming and forking from a message.
- **Give the agent tools**: embedded browser with controlled DOM/JavaScript access, parallel terminals, file exploration/editing, code search, Git change previews, screenshots, mouse, keyboard and embedded Python.
- **Stay in control**: toggleable skills, MCP servers, Plan/Execution modes, permission requests, optional sandbox, interactive questions and agent task tracking. `AGENTS.md` conventions and `SKILL.md` skills can load from the project.
- **Recover useful context**: conversation or shared memory, RAG search with a local multilingual model or embedding provider, images and a fallback vision model, token counts, speed and context compaction.
- **Customize your workspace**: per-project scheduled tasks, light/dark themes, French/English, custom name and logo, Markdown export and SQLite with EF Core migrations.

**Cloud providers** require network access and, depending on the service, an API key; the chat model does not run inside the EXE. Optional integrations retain their prerequisites: Git for Git view, Docker/Podman for sandbox, OpenCode for its dedicated connection, or Chrome/Node.js for Chrome MCP. The embedded browser uses the system web engine (Edge WebView2 on Windows, WebKit on macOS). The application core does not require a separate OhMyHarness server.

The guides for [agents](agent-modes.md), [browser and RAG](browser-rag-agents.md), [memory](memory.md), [custom skills](skill-authoring.md) and [sandbox](sandbox.md) describe these features and their limits.

## Getting started

### Complete design: from idea to verified delivery

Enable **Complete design** (**Conception complète** in French) in **Settings → Skills**, the GUI's **+ → Skills** menu, or the CLI's `/skills` picker. Describe your idea or the outcome you want. The agent asks focused questions, suggests alternatives with tradeoffs, helps choose technologies and a visual direction, then maintains a checklist through implementation and verification. Simple requests keep a lightweight workflow.

The skill uses existing capabilities: attach your project sources and enable the source/terminal tools you need. Plan mode stays read-only; switch to Execution for implementation. It can propose independent subagents, but delegation still requires enabled orchestration. The parent integrates their work and performs execution tests. Visual verification uses available browser, screenshot or vision tools; the CLI can use configured MCP tools. Missing capabilities and checks that could not be run must be reported, rather than presented as successful validation. Enabling this skill does not automatically enable other skills or grant permissions.

### Connect a provider

Launch `artifacts\GUI\OhMyHarness.App.exe`, then **Settings → Providers**. Each connection has its own card, key, URL and catalog. **Test connection** detects models, selects them all and automatically saves the provider. **Refresh models** preserves existing choices; manual edits are committed with **Save**. The chat picker groups checked models from all connections and automatically switches providers. Multiple connections of the same type stay independent. For APIs without `/models`, a model can be entered manually in the editor and selected in the card.

**Scheduled tasks**, within a project, creates a CRON schedule with a picker, instruction, model, thinking, resources, skills and a choice of fresh conversation or continued history. Tasks run while the application is open; they do not wake the PC.

**Settings → General** selects French or English. Language applies after Save and persists across launches; project names and conversation content are not translated. In the GUI composer, **Enter sends**, **Ctrl+Enter inserts a newline** at the cursor or replaces the selection. For CLI keyboard shortcuts, see [the CLI guide](cli.md).

**General → Application name and logo** customizes the displayed name and loads a logo with preview. Settings are stored in SQLite; external logos copy into `branding/`, and paths stay relative to the portable folder. Copying the whole folder preserves customization. Themes include **Fly dark** and **Fly light**, inspired by official Airbus blue, and **Electric dark/light**. See [portable customization](branding.md).

**General → Response style** offers DEFAULT (unchanged), SHORT, PRAGMATIC, DETAILED and FUN. The saved preference also appears under CLI `/settings → Response style`. It applies to subsequent sends; explicit user requirements and permissions still take precedence.

**Ctrl + mouse wheel** adjusts interface fonts in 10% steps from 80% to 150%. **Ctrl + 0** restores 100%. Size persists in SQLite and applies to new messages, code, reasoning and Settings windows without disabling auto-scroll. Browser content keeps its own zoom.

On Windows WinUI, Settings opens in an independent window: agents continue generating and using tools in the background. Their permission requests remain available in the main window. Save applies changes; Cancel or closing the window discards Settings drafts.

**Settings → Skills** offers source exploration/editing, web search, terminal, mouse control, keyboard control, screenshots, code review, planning and summarization. Exploration and Web are enabled by default. Disabling a skill removes its tools; source editing includes reading. Remote navigation requires “AI browser access”; inspection/interaction also requires “DOM access and AI interaction”. Choices are global and saved in SQLite.

Presets include OpenAI (`https://api.openai.com/v1`, `gpt-4.1-mini`) and DeepSeek (`https://api.deepseek.com`, `deepseek-flash`). Image capability and context window are configurable: match the limit published for the selected model. The initial 128,000-token value is a user configuration, not automatic detection.

**+ OpenCode** creates a dedicated connection to a local OpenCode server. The application can connect to an existing `opencode serve` or automatically start the configured executable, import available models and keep a separate OpenCode session per conversation. The server password uses the same DPAPI storage as API keys. OpenCode agent tools can be disabled per connection; when enabled, requests use Allow once / Always allow / Deny, and permanent grants remain revocable in Settings.

## Features

- **+ → Mode**: Plan or Execution per conversation. Plan technically blocks writes, terminal execution, interactions and MCP. **+ → Subagent orchestration**: Disable / Auto / Forced, with delegated analyses and results retained in chat.
- **AGENTS.md**: automatic convention loading from attached roots and subfolders. Discovery is asynchronous and best effort. **Custom skills**: `skills/` beside the executable, supplied `exemple-revue` template and on-demand loading of enabled `SKILL.md` files. See [modes, subagents and custom skills](agent-modes.md) for limits and OpenCode integration.
- **Tools** panel on the right with Web, Terminal, Git and Files tabs. **⛶** expands it to the entire workspace; click again to restore it. In a narrow window, the panel automatically fills the workspace and **×** returns to chat.
- Composer with **+** at bottom left (images, source folder, quick skill toggles, templates, settings) and an integrated Send button on the right. A template fills the draft without sending; replacing an existing draft requires confirmation.
- **Settings → Templates**: edit, create, delete templates and reset the **Web app** template, which requests a standalone HTML file without dependencies. Edits save to SQLite; Cancel discards them.
- **Terminal**: PowerShell commands in the project folder, output/exit code, Stop and a default 30-second timeout configurable by the AI using `timeout_seconds` from 1 to 600 seconds. `start_terminal` returns immediately so a server can run in the background; `read_terminal`, `wait_terminal` and `stop_terminal` monitor or stop it. Servers must stay in the foreground of their command (no shell detachment) and stop at the requested deadline. In sandbox, they also stop when generation ends. Each command uses a fresh non-interactive session; `cd` and variables do not persist between commands. Enable the Terminal skill for the AI to propose a command, subject to the configured permission policy. Commands manually launched with Execute use the Windows account's rights and are not sandboxed.
- **Git**: available when the project root contains `.git` (directory or worktree file). It lists modified files and shows changed, staged and unstaged lines read-only. Untracked files are listed without showing content. Git must be installed and on PATH.
- **Files**: browse the project folder, return to the parent, read text files and request opening them in Web.
- **Local Web**: folder button, absolute address-bar path or `open_local_file` AI tool. An approval window shows the file and resource directory; accepting permits HTML/JS rendering and resources from that directory in a dedicated local origin. Network paths, symbolic links/junctions, paths escaping the folder and excluded files are blocked. Denying neither reads nor navigates to the file.
- Sensitive access offers **Allow once**, **Always allow** or **Deny**. Permanent grants are limited to the displayed file, folder or site and saved in SQLite. **Settings → Permissions** lists scopes and allows revocation. Explicitly excluded files remain blocked. Camera/microphone/location permissions are never granted on the native Windows browser host.
- **Settings → Permissions → Permission request behavior** applies a global rule before dialogs: **Deny all**, **Ask** (default) or **Automatic approval**. Deny all and Automatic approval take precedence over saved permanent grants. Path protections and secret exclusions remain active in all three modes.
- Projects with independent conversations; creation, renaming and deletion.
- Multiple conversations can generate simultaneously (OpenAI-compatible, DeepSeek and OpenCode), including across projects. An animated bar appears beneath active conversations. Navigation, Settings and drafts remain available; **Stop** affects only the displayed conversation. Each send keeps its provider, model, sources and images. Drafts and attachments remain associated with the chat during the application session. Shared browser/desktop tools execute sequentially to avoid collisions; permission requests wait for the previous dialog to close.
- **Keyboard control** also exposes `keyboard_keys`, listing available keys, aliases and examples. `Alt`, `Ctrl`, `Shift` and `Win` work alone, as do shortcuts such as `Ctrl+S` and `Alt+Tab`. Both `Entrée` and `Enter` are accepted. A press immediately releases the key; it does not hold a modifier between calls.
- Persistent history and restoration of the last project, conversation and provider.
- SSE streaming, generation cancellation and retention of interrupted answers (excluded from subsequent requests).
- **Model reasoning** opens during streaming and scrolls its own contents down on updates, independently of the main conversation's scroll position.
- Up to four PNG/JPEG/WebP images per message, maximum 8 MB each; images are stored in SQLite and sent as multimodal content.
- Multiple source folders can be attached to one project and shared by its conversations. Each appears as a removable chip and separate root in Files; the AI can list subfolders and read text on demand. No full index or automatic transmission of whole folders.
- Embedded WebView2 browser. Enable **AI browser access** to open URLs and read pages. Also enable **DOM access and AI interaction** to expose a sanitized DOM with interactive elements, then permit clicks, typing, selections and scrolling. **Mouse control** accepts left/right clicks, double-clicks, movement and wheel input in the browser and on the Windows multi-monitor desktop. After an approval dialog, the previously active window is restored before desktop capture/action. Desktop captures draw the cursor recorded before the dialog; web captures show the latest manual or controlled WebView2 position and normalize it to the CSS pixels used by clicks. **Keyboard control** types into the active control and sends a key or shortcut (`Ctrl+S`, `Alt+Tab`, `Enter`, function keys) in the browser or Windows; focus the target first. **Screenshots** adds visual analysis; each keyboard/mouse interaction or image transmission requests approval for its scope.
- Tool loop limited to twelve model calls by default. **Settings → General → Automatically continue after 12 steps** continues without sending “continue” until the final answer or **Stop**. Permissions remain active and compaction may summarize old tool groups during long runs.
- **Settings → MCP**, after Skills: create, edit, delete and test multiple MCP servers; stdio, Streamable HTTP and SSE transports. **+ → MCP** toggles them quickly. See [MCP configuration](mcp.md).
- Tokens/second: estimated `≈` during streaming, then replaced by API-reported output tokens divided by stream duration. Latency before the first delta is excluded. Reasoning tokens are included if the provider counts them in `completion_tokens`.
- Context: live token count during the answer. `≈` marks local estimation before exact provider counters arrive; the display adds input and current output. At 95% of the configured limit, the application automatically summarizes older complete turns, keeps recent exchanges and saves the summary for subsequent calls. Compaction makes an additional request to the selected provider.
- French/English interface, dark/light themes, collapsible sidebar and responsive browser layout.

## Data and privacy

The native embedded browser is scoped per conversation: page, in-session navigation history, cookies, web storage and local-preview access are separated. Switching conversations displays its browser without redirecting background agents' calls. Profiles are stored in `WebView2/chat-<id>/` in WinUI and Chromium partitions `omh-browser-chat-<id>` in Electron. The old shared profile is retained but not shared with new profiles: log into sites again per conversation. Terminals are also scoped per conversation; Git and Files use the associated project's sources and reject stale results from a previous selection. Two chats attached to the same real folder still share its files. Real desktop mouse, keyboard and screenshots remain PC resources with existing permissions.

**Export**, beside **Tools**, copies the conversation as Markdown up to 512 KiB UTF-8. Above that, or if the clipboard is unavailable, it offers a `.md` file. Export includes all retained messages (including turns before compaction), stored reasoning, tool calls/results, counters and base64-embedded images. Image display depends on the Markdown reader. An active generation continues and its current text is included as a partial snapshot. Exported settings reflect export time, not per-message history. API keys and configuration secrets are excluded; exchange content and tool results are retained unchanged.

`database.sqlite`, in the same folder as `OhMyHarness.App.exe`, contains configuration, application navigation state, conversations, tool messages, images and permanent grants. **EF Core applies migrations at startup** through `Database.MigrateAsync()`; migrations and snapshots are versioned. If the file does not exist, a new database is created with application tables and initial values. No old AppData database is imported automatically.

The executable directory must be writable. For portable use, put the EXE in a user folder rather than `Program Files`.

OhMyHarness-managed data is grouped in this folder: `database.sqlite` (and SQLite journals), `skills/`, `MCP.json`, `WebView2/` for native Windows, `Browser/` for older external Chromium profiles, `sandboxes/`, `temp/`, Python and `OpenCodeWorkspaces/`/`opencode-runner.mjs` work files. There is no AppData fallback if the directory is unwritable. Old AppData folders are no longer copied automatically.

To back up or move the application, close all instances and copy **the entire folder**. Attached source folders remain references to external projects. External software (OpenCode server, MCP, Docker/Podman and executed commands) retains its own installation/storage; portable mode is not a system sandbox. The single-file .NET runtime may extract components into the system temporary cache. Keys remain tied to the system account as described below.

API keys are protected by **Windows DPAPI / CurrentUser** or the **macOS Keychain**. Copying the database to another account or OS requires re-entering keys; old Electron keys must also be re-entered in Uno. The rest of the database is not encrypted. Data actually used (messages, images, read files and read pages) is sent to the selected provider when sending.

The source-editing skill can create/modify files in the attached folder; without it, source tools remain read-only. Outside paths require one-time approval. Symbolic links/junctions, `.env*`, `secrets.json`, `.git`, `bin`, `obj`, `node_modules` and certain build directories are excluded from source tools and local preview. Text reads are limited to 128 KB, a web-preview resource to 32 MB. These restrictions do not sandbox approved terminal commands.

Each conversation has its own browser view in Tools. Native Windows uses WebView2 with a per-conversation profile; Uno Desktop uses Uno WebView2 (Edge WebView2 on Windows, WebKit on macOS). Uno Desktop profiles may be shared between views. Chrome · MCP remains an optional external mode. Camera/microphone/location and download blocking is applied on native Windows; Uno Desktop does not expose the same WebView2 events.

## Build and publish

Development prerequisites: Windows 10 1809+ / Windows 11, .NET 10 SDK, Git LFS for the local RAG model, Windows SDK and WinUI development tools installed through Visual Studio. Open `OhMyHarness.slnx` in Visual Studio/Rider or use:

```powershell
dotnet build src/OhMyHarness.App -c Release
dotnet run --project src/OhMyHarness.App -f net10.0-desktop -c Release
dotnet run --project tests/OhMyHarness.Tests -c Release
.\publish.ps1 -OutputDirectory artifacts\GUI
.\publish-cli.ps1 -OutputDirectory artifacts\CLI
```

`run.bat` and `publish.bat` provide equivalent double-click actions. The x64 GUI publication uses Uno Desktop and contains **one EXE** with embedded .NET and application components. Components extract automatically on first launch. The embedded browser uses Edge WebView2 on Windows; Chrome or Edge is required only for external Chrome · MCP mode. Native WinUI remains available through `publish.ps1 -NativeWinUI`.

Repository publication folders are `artifacts\GUI` and `artifacts\CLI`; temporary test publications belong under `artifacts\TEMP` and must be cleaned up. Pass `-OutputDirectory` explicitly. Storage uses the real process path because `IncludeAllContentForSelfExtract` redirects `AppContext.BaseDirectory` into the extraction cache. `tests/verify-portable.ps1` checks an actual publication and relocation of its EXE.

`publish.ps1 -Runtime win-arm64` targets ARM64 (not validated on ARM64 hardware). Do not remove `IncludeAllContentForSelfExtract` from the publish profile.

To add a migration:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.9
dotnet ef migrations add MigrationName --project src/OhMyHarness.Core --output-dir Migrations
```

## Structure

| Project | Purpose |
| --- | --- |
| `src/OhMyHarness.Core` | EF Core, agents, providers, Windows/macOS tools, CRON, compatibility JSON service in `Hosting` |
| `src/OhMyHarness.App` | Uno Platform Windows/macOS interface, browser, settings and tasks |
| `src/OhMyHarness.Cli` | Terminal interface and non-interactive automation |
| `desktop` | Legacy Electron interface and integration tests, retained for compatibility |
| `OhMyHarness.Desktop.slnx` | Portable solution without WinUI dependency, for macOS |
| `tests/OhMyHarness.Tests` | Offline test executable without API keys |

## Validation and limitations

Tests cover Unicode/CRLF streaming, fragmented tool calls, provider usage, truncated streams, cancellation, URLs, source protections, repeated migrations, image persistence, cascading deletes and key encryption. API errors are displayed without logging keys.

Real OpenAI/DeepSeek calls require a user key; automated validation uses a simulated provider. Answers render Markdown. Semantic RAG must be enabled and sources indexed; terminals run non-interactive commands. Highly dynamic sites may need another read after loading. Tests also cover custom templates, PowerShell commands/cancellation, Git diffs and local-preview path protections.

References: [OpenAI Chat Completions](https://developers.openai.com/api/reference/resources/chat), [DeepSeek API](https://api-docs.deepseek.com/), [DeepSeek vision](https://api-docs.deepseek.com/guides/vision/), [WinUI 3 single-file publishing](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app).

- Structured tasks, per-conversation interactive questions and protection after three identical calls: [agent workflow guide](workflow.md).

Chat follows new answers only while scrolled to the bottom; reading history pauses tracking. The web skill exposes `browser_javascript` (with browser and DOM access enabled) to read scripts/variables or modify page JavaScript after approval. Code is synchronous, limited to 32,000 characters and 5 seconds, and runs in the conversation's page without Node access. Changes are temporary until reload; use source tools to save them. The tool is prohibited in Plan mode and the sandbox.

- On-demand browser startup, Chrome MCP, expanded file reads, Auto tracking, code highlighting, configurable RAG and subagent views: [guide and limits](browser-rag-agents.md).
- Per-conversation resources, default folders, `permission.json`, resume/fork, TODO, two Git views and HTTP/DeepSeek compatibility: [conversation guide](conversation-workspace.md).
