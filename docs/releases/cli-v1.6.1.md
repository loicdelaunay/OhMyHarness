# OhMyHarness CLI v1.6.1

The first standalone CLI release brings the OhMyHarness agent engine to a compact terminal workspace. Choose GUI or CLI: the existing [GUI v1.0.0 release](https://github.com/loicdelaunay/OhMyHarness/releases/tag/v1.0.0) remains available separately.

## Download and start

Download `OhMyHarness-CLI-v1.6.1-win-x64.zip`, extract it to a writable folder, and launch `omh.exe` from your project directory. The Windows x64 executable includes its .NET runtime, Python runtime and multilingual embedding resources; no separate .NET or Python installation is required.

Run `/connect` to choose OpenAI, DeepSeek, an OpenAI-compatible/local API, or OpenCode. Enter credentials, auto-detect models or enter their IDs, then confirm. Type `/` for suggestions; arrows select and Tab/Enter completes a command. Run `/theme` to preview palettes live before saving.

## What is new since GUI v1.0.0

- A compact terminal chat with streaming output, searchable commands, multiple conversations, agent tools, approvals, interactive questions and subagents.
- Inline slash completion and guided provider setup with manual model entry when discovery is unavailable.
- CRT Green, CRT Amber and Neon Synthwave themes, live palette previews, and optional Windows Terminal font/CRT profiles. Shared Electric dark/light themes and theme contrast improvements.
- Plain-text and JSON automation with `omh run`, Plan/Execution modes, queue/steering, source attachments, terminals, Git diffs, memory, RAG, MCP and Markdown exports.
- Favorite conversations, optional AI naming, response durations, and improved reading position during streaming.
- Asynchronous nested project instruction discovery, configurable portable logs, and vision-model guidance with component coordinates.
- Fresh SQLite workspaces beside the executable without importing previous AppData conversations.

The repository also includes updated GUI queue controls, streaming and appearance improvements. This release's binary is **CLI only**; build the GUI from source for its changes since the existing GUI download.

## Included files and requirements

The archive contains only `omh.exe`, `README.txt` and the MIT `LICENSE`. No conversations, database, API keys or user configuration are included. Use `SHA256SUMS.txt` to verify the archive.

Configure a model provider to chat; the chat model is not bundled. Git, Docker/Podman, OpenCode and MCP servers have their own optional prerequisites. The CLI filters unavailable graphical desktop tools; browser automation can use MCP. Windows Terminal or another Unicode/truecolor terminal is recommended.

## Validation

- 117 CLI checks passed, covering theme previews, slash completion, connection setup, rendering sizes and headless runs.
- 511 shared-engine checks passed.
- Published Windows x64 executable verified with `--version` (`1.6.1`).
- No macOS binary or macOS runtime validation is included.

See the [CLI guide](https://github.com/loicdelaunay/OhMyHarness/blob/main/docs/cli.md) and [full changelog](https://github.com/loicdelaunay/OhMyHarness/blob/main/CHANGELOG.md).
