# Windows and macOS with Uno Platform

The GUI solution uses two projects: `OhMyHarness.Core` and `OhMyHarness.App`. The latter uses Uno Platform to share the interface, settings, conversations and tools between Windows and macOS. The former JSON service lives in `Core/Hosting`; the Electron `desktop` folder is retained for compatibility and is not required by the new publication. A separate `OhMyHarness.Cli` project provides the terminal interface.

See [the Uno, tasks and models guide](uno-tasks-models.md) for architecture and features.

## Build and publish

On Mac, install the .NET 10 SDK and Xcode command-line tools:

```bash
dotnet build src/OhMyHarness.App -f net10.0-desktop -c Release
dotnet run --project src/OhMyHarness.App -f net10.0-desktop -c Release
dotnet run --project tests/OhMyHarness.Tests -c Release

bash ./publish-macos.sh arm64 # Apple Silicon
bash ./publish-macos.sh x64   # Intel
```

The standalone publication is under `artifacts/release/osx-arm64` or `osx-x64`, with the `OhMyHarness.App` launcher. It requires neither installed .NET nor Electron. The script does not produce a signed DMG or configure Apple notarization. For public distribution, sign and notarize on Mac; retain native dependencies supplied with the publication.

On Windows, `publish.ps1` publishes Uno Desktop by default. Follow repository conventions with `./publish.ps1 -OutputDirectory artifacts/GUI`. `-NativeWinUI` targets native WinUI; validate startup on the target machine. Temporary validation publications belong in a dedicated folder under `artifacts/TEMP` and must be cleaned up afterward.

## Platform adaptations

| Feature | Native Windows | Uno Desktop / macOS |
| --- | --- | --- |
| Interface, models, CRON, history, agents, skills | Shared interface | Shared interface |
| Browser | Embedded WebView2 | Uno WebView2 in the Web tab (WebKit on macOS) |
| DOM, JavaScript, web capture and keyboard | WebView2 API | DOM scripts; capture the view from the application window |
| Local previews | Approved virtual origin | Loopback server limited to the approved folder |
| Terminal | PowerShell | zsh on Mac, PowerShell on Windows |
| Mouse and keyboard | Windows APIs | CoreGraphics on Mac |
| Desktop/application capture | Windows APIs | `screencapture`, target by window ID, cursor overlay |
| API keys | DPAPI CurrentUser | macOS Keychain |

The Uno Desktop embedded browser opens in Tools and keeps a view per conversation. Chrome/Edge and Node.js are needed only for external Chrome · MCP mode. On macOS, synthetic JavaScript interactions may be refused by sites requiring native events; browser capture uses the system Screen Recording permission. The macOS flow still needs validation on a Mac. Git, OpenCode, Docker and MCP servers are required only for features that use them.

Data (`database.sqlite`, `skills/`, `MCP.json`, profiles, Python and temporary files) stays in the executable's portable directory. Put the publication in a writable folder. Keys are tied to the system account: keys from Windows DPAPI or the old Electron interface must be re-entered in Uno on Mac. EF Core migrations preserve the rest of the history. Do not open an older version against the migrated database without a backup.

Under **System Settings → Privacy & Security**, allow **Accessibility** for mouse/keyboard and **Screen Recording** for captures. Application permissions are still required and do not replace macOS rights. `keyboard_keys` describes keys available on the OS.

## Validation

Native Windows and Uno Desktop builds, 413 Core checks, both standalone EXEs and nine Chromium checks were verified during the migration. The UI test also checks model checkboxes and two task executions with tools and history using a simulated local provider. macOS ARM64 and Intel targets were compiled from Windows. `.github/workflows/desktop.yml` includes Windows, Mac ARM64 and Mac Intel builds and tests.

**Still to verify on a real Mac:** startup and shutdown, pickers, Keychain after restart, TCC permissions, Command/Option keys and Unicode, cursor and coordinates in Retina/multi-monitor captures, embedded Python startup, signing/notarization. Cross-compilation does not validate these native interactions.

Reference: [Uno Desktop publishing](https://platform.uno/docs/articles/uno-publishing-desktop.html).
