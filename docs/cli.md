# OhMyHarness CLI

The same agent engine in a terminal workspace. The CLI is a separate .NET 10 project, <code>src/OhMyHarness.Cli</code>, referencing <code>OhMyHarness.Core</code>. Its full-screen interface uses truecolor ANSI rendering, adapts to terminal width, and supports Windows and macOS builds.

The header shows a compact terminal rendering of the OhMyHarness icon and the configured application name. The icon is drawn with Unicode half blocks and ANSI colors, so it remains visible without a terminal image protocol or an external image file. On Windows, `omh.exe` also carries the application icon in Explorer.

The interaction takes inspiration from the command palette and automation mode of [OpenCode](https://opencode.ai/v2/docs/cli) and the terminal agent workflow of [Antigravity CLI](https://www.antigravity.google/product/antigravity-cli). This is an OhMyHarness client with its own shared engine.

## Start

~~~powershell
dotnet run --project src/OhMyHarness.Cli -- "E:\Projects\MyProject"
~~~

With a published build:

~~~powershell
.\omh.exe "E:\Projects\MyProject"
.\omh.exe --database "L:\OHM2\database.sqlite" --chat 12
~~~

Without a project argument, the current working directory becomes the project's source folder. <code>--chat ID</code> resumes an existing chat without attaching the current directory. Each project folder is reused when already registered.

SQLite, skills, MCP configuration, RAG models and Python resources live beside the executable by default. <code>--database</code> explicitly selects another workspace and locates its resources beside that database. It can point to the desktop app's database to reuse providers, conversations, skills and memory. No AppData database is imported. API keys use Windows DPAPI/macOS Keychain; copying a workspace to another OS/account requires re-entering keys. Legacy Electron macOS keys need to be re-entered.

Use a terminal with Unicode and truecolor support, such as Windows Terminal. The minimum viewport is 56 columns × 18 rows. A compact header shows the provider, model and project folder above a single-column transcript. The composer sits between two thin separators, with model, speed and context usage below it. Use `/chats` to switch conversations and Ctrl+P for the searchable command list.

## Connect a provider

`/connect` guides you through four steps:

1. Choose **OpenAI**, **DeepSeek**, **another OpenAI-compatible API**, **a local compatible API**, or **OpenCode**.
2. Enter the masked API key and confirm the API URL. Local APIs can omit the key; OpenCode uses its server username/password.
3. Auto-detect models or enter their exact IDs separated by commas, then choose the default model. If discovery fails, retry or enter models manually. All entered/detected models become available in `/models`.
4. Name the connection, review the summary, and select **Save and use**.

Nothing is saved until the final confirmation. Escape cancels the setup. Manual model entry does not test the credentials or the model's availability; auto-detection checks the model-list endpoint.

## Everyday workflow

Type `/` in the composer to see command suggestions. The list filters as you type (`/co` shows `/connect`, `/context`, and `/compact`). Use ↑/↓ to select, then Tab or Enter to complete the command. Add any arguments and press Enter to execute. Escape dismisses the suggestions without stopping an active response. Completion stays out of ordinary messages and command arguments.

| Command | Action |
| --- | --- |
| /connect, /providers | Guided provider setup, including DeepSeek and OpenCode; manage keys and remove providers |
| /models | Search selected models across providers; refresh detects and selects the current connection's models |
| /project, /new, /chats | Switch projects and conversations while other runs continue |
| /favorite, /name | Pin/unpin the selected chat; generate a title using the configured naming model |
| /attach path | Attach a source file or folder to this conversation |
| /image path.png, /clear-images | Attach up to four PNG/JPEG/WebP images (8 MB each) or clear pending images |
| /mode | Plan/Execution with the shared engine's tool enforcement |
| /agents | Disabled/Auto/Forced orchestration and subagent transcripts |
| /skills | Toggle built-in and project skills |
| /mcp | Toggle servers or import MCP JSON after reviewing it |
| /queue | Edit, delete, steer or resume queued messages |
| /steer instruction | Supply input at the next agent boundary |
| /git | List changed files and open diffs |
| /terminal command | Start a conversation-scoped command in the background |
| /terminal | List terminals, read output, refresh, stop or close |
| /context, /compact | Inspect token usage and compact manually |
| /tasks, /memory keyword | View task lists and search scoped memory |
| /templates, /fork, /export | Fill the composer, branch history, export Markdown |
| /sandbox | Enable Docker/Podman isolation, review a diff and apply approved changes |
| /details | Expand/collapse reasoning and tool output |
| /settings | Language, appearance, thinking, permissions, AI naming, vision bridge and logs |
| /font | Independent font and size, bundled retro fonts, terminal profile export/install |
| /update | GitHub update check, automatic startup-check preference and installation |

Existing OpenCode and composed providers configured in the desktop app are usable. Source exploration, glob/grep, patches, asynchronous terminals, memory, RAG, Python and custom skills use the same engine. Sending another message during a response queues it.

The timeline distinguishes user, assistant and tool messages, colors code fences, and displays response speed and context usage. This is lightweight terminal Markdown rendering. User and tool content is sanitized before reaching the terminal.

Favorite chats stay at the top with a star. AI naming is off by default; choose a provider/model in settings before enabling it or using <code>/name</code>. It sends a short text excerpt after the first successful answer. OpenCode naming requires its service to be running. Completed responses include their generation time. Queued lines expose clickable edit, delete and steer icons; <code>/queue</code> remains available from the keyboard.

Vision settings accept custom instructions and a component mode with labelled rectangles/polygons. The agent can override these per <code>analyze_image</code> call using <code>instruction</code> and <code>mode</code>. Bounds are approximate image coordinates, not direct screen coordinates or actual image crops.

Diagnostic JSONL logs live in <code>logs</code> in the portable data directory. Settings control minimum severity, retention (seven days by default) and disabling. Logs exclude prompts, response bodies, keys and tool arguments. Nested project instructions load asynchronously, skipping inaccessible files, generated/dependency directories and instruction files above 32 KB; total loaded instructions remain limited to 64 KB without failing the run.

| Key | Action |
| --- | --- |
| Ctrl+P | Searchable command palette |
| Ctrl+N / Ctrl+O / Ctrl+B | New conversation / chat search / models |
| Enter | Send or queue |
| Ctrl+J or Alt+Enter | Newline (Ctrl+Enter where the terminal supports it) |
| Arrows, Home/End, Backspace/Delete | Move and edit; Home/End stay within the current line |
| Ctrl+A | Select the entire draft or dialog input |
| Shift+arrows / Ctrl+Shift+Left/Right | Extend selection by character / word |
| Ctrl+Left/Right, Ctrl+Home/End | Move by word / to the beginning or end of the draft |
| Ctrl+Shift+Home/End | Select to the beginning or end of the draft |
| Ctrl+Backspace (explicit modifier) / Ctrl+Delete | Delete the previous / following word (Ctrl+W also deletes the previous word) |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut selected text / paste text without sending (Windows and macOS) |
| Ctrl+Z / Ctrl+Y | Undo / redo; Ctrl+Shift+Z also redoes where supported |
| Ctrl+C without selection | Stop the current run |
| PgUp/PgDn or mouse wheel | Scroll history or dialog details |
| End with an empty composer | Follow the latest output |
| Escape | Cancel a dialog or stop the selected chat |
| Ctrl+Q | Quit; active runs require confirmation |
| Ctrl+L | Redraw |

Selection is highlighted; typing or pasting replaces it. Masked credential fields support selection and paste but cannot be copied or cut. Shortcuts reserved by the terminal host must be released in its own settings before the CLI can receive them. Pasting also supports the terminal's bracketed-paste protocol. Agent questions appear as choices or free-text dialogs; multiple selections are supported. Other conversations continue while a question or permission is open. Permission choices are Deny, Allow once and Always allow this scope.

## Automation

~~~powershell
.\omh.exe run "Review this repository" --project .
.\omh.exe run "Implement the requested fix" --project . --execute
.\omh.exe run "Explain the architecture" --project . --json
Get-Content prompt.txt -Raw | .\omh.exe run --project .
~~~

Each run creates a new conversation unless <code>--chat ID</code> is supplied. Non-interactive runs default to Plan. <code>--execute</code> enables modifying tools; sensitive operations are still denied unless <code>--allow-tools</code> is explicitly supplied. This override does not rewrite the shared permission policy. Project restrictions still apply.

Plain mode prints the answer to stdout and status to stderr. <code>--json</code> emits newline-delimited events: started, message, stream, status, subagent, question, done, etc. Ctrl+C cancels and preserves partial history.

Exit codes: <code>0</code> success, <code>1</code> error, <code>3</code> agent question requiring interactive input, <code>130</code> cancelled. To continue a needs-input run, reopen the chat using the chatId from its JSON event and send your answer. Pending questions are process-local.

## Scope

Project instructions, tool-loop protection, compaction and provider compatibility handling come from the shared engine. The CLI does not instantiate the desktop WebView, desktop input/capture host, or scheduled-task scheduler. Browser automation can be provided by MCP (including Chrome DevTools configured in the desktop settings). Graphical-only built-in skills are filtered per run without changing desktop preferences. OpenCode requires an OpenCode service.

Advanced RAG, vision and composed-provider configuration remains in the desktop settings; the CLI uses those persisted settings. GUI and CLI processes can share history but do not coordinate active-run ownership across processes: avoid running the same conversation in both at once.

## CLI themes, fonts and CRT

Use `/theme` (or Settings → Theme) to select **CRT Green**, **CRT Amber**, **Neon Synthwave**, any desktop palette, or **Follow desktop theme**. The CLI choice is saved separately from the desktop theme. Colors, separators, header styling and block cursors apply immediately without changing message/code content.

Browse the theme list with ↑/↓ to preview each palette immediately, including when filtering the list. Enter saves the selection; Escape restores the previous theme. The preview also works when the session was started with `--theme`.

```powershell
omh --theme crt-green
omh --theme crt-amber
omh --render-demo --theme neon-synthwave
```

`/font` selects a font **independently of the theme**, plus a size from 8 to 36 pt. Choose the bundled **VT323**, **Share Tech Mono** or **Space Mono**, a system font, or enter another installed font name. The original OFL licenses travel with the embedded fonts. A terminal application cannot universally change its host's font: saving the choice alone does not change the active tab.

Choose **Save and install font + profile** on Windows to install the bundled font for the current user and create a dedicated Windows Terminal profile. Open **OhMyHarness · …** in a new tab (restart Windows Terminal if needed) to apply the font and experimental CRT scanlines/glow. **Export** writes the profile and font/license beside the portable database; macOS users can install the exported TTF with their font manager. Missing system/custom fonts use the host terminal's fallback. Fonts installed for Windows are machine-local, under the user's Windows Fonts directory and HKCU font registration; exported copies remain portable.

The portable JSON is kept under `terminal-profiles` beside the database. Installation adds only an OhMyHarness fragment under `%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments\OhMyHarness`; it does not rewrite `settings.json` or change your default terminal profile. Delete the exported fragment there to uninstall it. Regenerate after moving the portable application: launch paths are absolute. This optional terminal integration is machine-local; other application data remains portable.

macOS and other terminals still display the theme's ANSI colors and borders; select fonts in that terminal's preferences. CRT glow is host-dependent, not simulated with flashing text. See [Windows Terminal appearance](https://learn.microsoft.com/en-us/windows/terminal/customize-settings/profile-appearance) and [profile fragments](https://learn.microsoft.com/en-us/windows/terminal/json-fragment-extensions).

## GitHub updates

The interactive CLI checks for compatible updates at startup by default. `/update` lets you disable that check, check manually, or download and restart into a newer release. Non-interactive automation never checks or installs updates. The GUI has its own equivalent setting in **Settings → General**.

Installation is user-triggered after the automatic check. The app waits for agents and drafts to be finished, verifies the official release asset's SHA-256, and replaces only its executable after shutdown; terminals/tools close with the app. SQLite, skills, MCP configuration and other user resources are preserved. A hidden Windows PowerShell helper performs the replacement without administrator rights. If another instance stays open, it cancels instead of stopping it. Save GUI settings before choosing to restart.

Automatic installation requires a published Windows standalone executable in a writable folder. Staging, the previous EXE (`previous.exe`), and installation result/error files live in `.updates` beside that EXE. They can be removed after a successful update once the old version is no longer needed. The backup covers the executable, not database migrations. Other platforms and development builds use manual downloads.

Release maintainers: GUI tags must use `vX.Y.Z` or `gui-vX.Y.Z`; CLI tags use `cli-vX.Y.Z`. Assets must be named `OhMyHarness-…-win-x64.zip` (GUI), `OhMyHarness-CLI-…-win-x64.zip` (CLI), or the corresponding `win-arm64`/`.exe` variant. ZIPs must contain exactly one `OhMyHarness.App.exe` or `omh.exe`, respectively. Other archive contents are not installed. Drafts, previews, older versions, mismatched architectures and assets without a GitHub SHA-256 digest are excluded. Publish each channel's executable separately; the updater does not rely on GitHub's global “latest” release.

## Publish and validate

~~~powershell
.\publish-cli.ps1
.\publish-cli.ps1 -Runtime win-arm64
.\publish-cli.ps1 -OutputDirectory L:\OhMyHarnessCLI
~~~

On a Mac with .NET 10 and Git LFS:

~~~bash
git lfs pull
bash ./publish-cli.sh osx-arm64
# Intel Mac: bash ./publish-cli.sh osx-x64
~~~

Default output: <code>artifacts/release/cli/&lt;runtime&gt;</code>. The CLI uses the same bundled Python and local embedding model as the desktop app. Windows behavior is checked locally; macOS publishing/native integrations require a real Mac for runtime validation. Signing and notarization are separate.

~~~powershell
dotnet run --project tests/OhMyHarness.Cli.Tests -c Release
dotnet run --project tests/OhMyHarness.Tests -c Release
.\omh.exe --render-demo
~~~

The last command prints a deterministic ANSI example of the real renderer with clearly labelled fictional content. It makes no provider request and opens no database.

## Response style

Open `/settings` and choose **Response style**. DEFAULT adds no style instruction; SHORT asks for the minimum answer, PRAGMATIC for fairly short actionable answers, DETAILED for useful depth, and FUN for appropriate light humor. This preference is shared with GUI Settings → General and saved in SQLite. It applies to the next send, including OpenCode sessions, without changing an active generation or tool permissions.

Ordinary Backspace removes one character/grapheme, whether the terminal sends BS or DEL. If your host does not transmit a distinct Ctrl+Backspace sequence, use Ctrl+W to delete the previous word.
