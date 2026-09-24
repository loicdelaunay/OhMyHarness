# Uno Platform, scheduled tasks and models

The GUI application consists of two projects:

- `src/OhMyHarness.Core`: SQLite/EF data, migrations, providers, agents, tools, CRON scheduling and desktop adapters. The former JSON service remains under `Hosting` for tests and compatibility with the old interface.
- `src/OhMyHarness.App`: shared Uno Platform 6.7 interface in C#/XAML. Targets: `net10.0-windows10.0.19041.0` (Windows WinUI) and `net10.0-desktop` (Skia Windows/macOS). Test projects remain separate from the application solution.

The terminal interface is provided by the additional `src/OhMyHarness.Cli` project.

`database.sqlite`, skills, MCP.json, embedded Python, browser profiles and resources remain in the executable's portable directory. Migrations add tasks and catalogs without removing conversations. Do not run an older application against an already migrated database without a backup.

## Project tasks

Open **Scheduled tasks** in the sidebar. Create a task, enter its name and instruction, then choose a frequency: minutes, hours, daily, weekly or custom five-field CRON. The time zone is retained; the next three due times are previewed. CRON steps reset each hour/day (for example, `*/40` means minutes 0 and 40 of every hour).

**Model and thinking** and **Resources and tools** configure provider, model, thinking, context, image capability, folders/files, Plan/Execution mode, orchestration, sandbox, automatic continuation and skills. Folders can follow project defaults or be defined for the task. Current application and project permissions still apply: a task may wait for approval or an interactive answer.

- **Continue history**: reuse its last conversation with normal compaction.
- Option unchecked: create an empty conversation per run. Previous conversations remain accessible.
- An already busy conversation is not resumed in parallel. A task cannot overlap its own execution.
- Scheduling works **while the application is open**, including while Settings is open. It does not wake the computer or install a system service.
- On restart, multiple missed occurrences are combined into one run; the next is calculated from the current time. Interrupted runs are not immediately replayed before the next due time, to avoid repeating actions.
- Enable/disable, edit and delete from the task list. Deleting a schedule retains conversations and does not stop an active run; Stop remains available in its conversation.

Schedules are stored in UTC with their original time zone. A local lock prevents two instances from running the same tasks concurrently. No secret is copied into a task definition: it references an existing connection.

## Model catalog

Each provider card offers **Refresh models**, a list and checkboxes. In the editor, **Test connection** detects models, selects them all and immediately saves that provider. A simple refresh preserves selections still offered by the server; it does not automatically select new models, and changes are committed with **Save**. Connections sharing a name keep separate catalogs.

The chat picker groups checked models with connection name and identifier. Changing model also selects its provider. Unchecking every model hides that connection from the picker. The provider editor retains an editable identifier for APIs without `/models`: this model can be added to the visible catalog.

## Platform differences

Native Windows retains embedded WebView2. Uno Desktop also displays Uno WebView2 **inside the Web tab** of Tools, with one view per conversation. The browser starts on demand and its shutdown does not interrupt other panels. Navigation, DOM reading/editing, JavaScript, clicks, keyboard, screenshots and local previews remain available; Uno Desktop synthetic interactions may be limited on sites requiring native events. Windows Uno screenshots crop the Web view from the application window. Windows requires the Edge WebView2 runtime; on macOS, Uno uses WebKit. External Chrome · MCP mode requires Chrome/Edge and Node.js.

On macOS, new keys are stored in the system Keychain; SQLite holds only their reference. Windows DPAPI and old Electron keys must be re-entered when switching platform/host. Mouse/keyboard skills use CoreGraphics; application screenshots use window identifiers and include a cursor. macOS must grant Accessibility and Screen Recording. Application approval does not replace these system permissions.

The terminal uses PowerShell on Windows and zsh on macOS. Git, Docker and MCP servers remain prerequisites for their respective features. This migration targets Windows and macOS; the presence of Uno X11 does not establish Linux validation.

## Build and verify

```powershell
dotnet run --project tests/OhMyHarness.Tests -c Release
dotnet build src/OhMyHarness.App -f net10.0-windows10.0.19041.0 -c Release
dotnet build src/OhMyHarness.App -f net10.0-desktop -c Release
# Repository GUI publication:
.\publish.ps1 -OutputDirectory artifacts\GUI
# Temporary native WinUI validation; remove this dedicated output after testing:
.\publish.ps1 -NativeWinUI -OutputDirectory artifacts\TEMP\winui-check
```

On Mac: `./publish-macos.sh arm64` or `./publish-macos.sh x64`. The script publishes standalone Uno Desktop without Electron. Apple signing/notarization is still needed for signed distribution.

The repository property `-p:OhMyHarnessDesktopOnly=true` restricts RID builds to Uno Desktop without imposing this TFM on Core. Publishing scripts apply it. Cross-compilation example:

```powershell
dotnet build src/OhMyHarness.App -f net10.0-desktop -p:OhMyHarnessDesktopOnly=true -r osx-arm64 -c Release
```

An isolated UI test is available with `OHMYHARNESS_UI_SMOKE=<new temporary folder>`: it starts with a test database, checks model checkboxes and the picker, saves a task, runs two turns through a simulated local provider with tools/history, captures screens and closes only that instance. `dotnet run --project tests/OhMyHarness.Tests -c Release -- --browser-smoke` tests two headless Chromium profiles without using personal profiles.

References: [Uno SDK and shared project](https://platform.uno/docs/articles/features/using-the-uno-sdk.html), [Desktop publishing](https://platform.uno/docs/articles/uno-publishing-desktop.html), [WebView capabilities and limits](https://platform.uno/docs/articles/controls/WebView.html).
