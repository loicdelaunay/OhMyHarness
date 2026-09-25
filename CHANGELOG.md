# Changelog

## 1.8.1 — 2026-09-25

- GUI and CLI downloads now cover Windows x64, macOS Intel and Apple Silicon, with checksums and clean portable archives. macOS builds are unsigned; native interactions still need manual validation.
- Response duration in GUI messages is now shown only while hovering over the message.
- Restored local RAG support on Intel Macs by pinning ONNX Runtime to 1.23.2, which includes native libraries for both Intel and Apple Silicon Macs, as well as Windows.
- Fixed sandbox creation on Windows when a source folder contains an open application database. Excluded SQLite files are now filtered before reading their contents.
- Fixed GitHub Actions downloading only the Git LFS pointer instead of the bundled MiniLM weights. Builds now verify the model checksum before embedding it and explain how to retrieve missing or invalid weights.
- Fixed macOS CI tests failing on symbolic-link ancestors in system temporary paths by using the runner's physical temporary directory; sandbox link restrictions remain enforced. Updated the desktop service test to match the ten available themes.

## 1.8.0 — 2026-09-24

- Added a shared response-style preference in GUI General settings and CLI `/settings`: DEFAULT leaves prompts unchanged; SHORT, PRAGMATIC, DETAILED and FUN guide the length and tone of subsequent answers, including OpenCode sessions.
- Fixed CLI Backspace deleting a word in terminal hosts that send DEL. Both ordinary Backspace encodings now delete one character/grapheme; word deletion requires explicit modifiers or Ctrl+W.
- Moved update, automatic naming and logging settings to the bottom of the GUI General page.
- Translated all Markdown guides under `docs/` into English and moved the full user guide to `docs/guide.md`, updating navigation links.

## 1.7.1 — 2026-09-24

- Fixed CLI keyboard editing in the composer and text dialogs: Ctrl+A, Shift+arrows, Ctrl+arrows, Ctrl+Shift+arrows, Home/End and word deletion now preserve selections and Unicode characters. Modified VT key sequences are decoded instead of being discarded.
- Selected text is highlighted and replaced when typing or pasting. Added undo/redo, clipboard copy/cut/paste on Windows and macOS, and caret-aware rendering in long input fields. Ctrl+C still stops the active run when no text is selected; masked credentials cannot be copied or cut.

## 1.7.0 — 2026-09-24

- CLI fonts can now be selected independently from the color theme, with a separate size setting, a custom installed-font name, and bundled VT323, Share Tech Mono and Space Mono fonts under their original OFL licenses. `/font` exports the font/profile or installs them for the current Windows user; a new terminal tab applies the font.
- Added GitHub updates to GUI Settings > General and CLI `/update`, with separate automatic startup-check preferences. Each interface selects only stable releases for its channel and architecture. Installation downloads and verifies SHA-256, preserves user data, waits for the application to close, replaces only the executable and restarts. The previous executable is retained for recovery.
- Automatic installation is limited to published Windows standalone builds; no installation occurs during an agent run or with unsent drafts. Source builds and other platforms retain manual release downloads.

## 1.6.1 — 2026-09-24

- CLI theme selection now previews colors immediately while browsing or filtering. Enter saves the selected theme; Escape restores the previous palette, including themes passed through `--theme`.
- Updated the Windows CLI publication to include inline slash-command completion introduced in 1.6.0.

## 1.6.0 — 2026-09-24

- Added inline CLI slash-command suggestions, filtered as you type. Use arrow keys to select, Tab/Enter to complete, and Escape to dismiss without interrupting the agent. Suggestions include descriptions and adapt to small terminals.

## 1.5.0 — 2026-09-24

- Simplified the CLI to a compact header, a single-column transcript, a prompt between two separators and unobtrusive model/usage information. Command menus are plain searchable lists with command names and descriptions; conversations remain accessible through `/chats` and Ctrl+O.
- Rebuilt `/connect` as a guided flow: provider type (including DeepSeek, local/compatible APIs and OpenCode), masked credentials, automatic discovery or manual model IDs, default model and final confirmation. Failed discovery offers retry/manual entry. Cancelling leaves no partial connection; provider, visible models and selection are saved together.

## 1.4.0 — 2026-09-24

- Added CLI-specific CRT Green, CRT Amber and Neon Synthwave themes with distinct palettes, retro headings, square/double borders and block cursors. CLI appearance is saved independently of the desktop theme; `/theme` and `--theme` select it.
- Added `/font` to export or explicitly install a dedicated Windows Terminal profile with the theme's font and experimental CRT scanline/glow effect. Other terminals keep their own font while displaying the CLI colors and typography. Profiles are also exported beside the portable database and do not rewrite Windows Terminal's existing settings.

## 1.3.0 — 2026-09-24

- Added Electric dark and Electric light themes based on Midnight Black (#0E0F12) and Electric Cyan (#4CC9F0), available in the desktop app and CLI.
- Fixed text selection, accent label/button contrast, menu and dropdown surfaces, and selected/hovered list colors across all themes. Theme previews update shared control colors and activity glows immediately.
- The CLI now uses the complete theme catalog, including correct light palettes for Ivory and Mist.

## 1.2.0 — 2026-09-24

- Project instructions now load off the UI thread across nested folders without stopping conversations at 2,000 directories. Inaccessible or oversized instruction files are skipped.
- Added pinned favorite conversations, configurable AI naming after the first response, and manual AI naming from the conversation menu or CLI.
- Vision bridge now accepts custom guidance and an optional component/shape breakdown with image-relative bounds and polygons.
- Added portable diagnostic logs with severity filtering, configurable retention (seven days by default), and an off switch. Prompts, response content, keys and tool arguments are excluded.
- Queue actions are inline icon buttons; completed model messages show their duration. Streaming preserves completed Markdown blocks and pauses repainting while reading earlier content; the CLI also keeps the reading position anchored.

## 1.1.0 — 2026-09-24

- Added OhMyHarness CLI, a full-screen terminal workspace with searchable commands and conversations, concurrent streaming chats, model/provider selection, Plan/Execution modes, approvals, agent questions, task lists, subagent inspection, memory, MCP, Git diffs, terminals, attachments, queue/steering and Markdown exports. It reuses the shared agent engine and portable SQLite storage.
- Added non-interactive runs with plain text or JSON events, explicit execution/approval options, and standalone CLI publishing for Windows and macOS.

## 1.0.1 — 2026-09-23

- A standalone executable started in a new folder now creates a fresh portable database instead of silently copying conversations and settings from a previous AppData installation. Legacy browser and OpenCode workspace folders are no longer imported automatically. Existing `database.sqlite` files beside the executable remain untouched and continue to migrate normally.
