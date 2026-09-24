# OhMyHarness CLI v1.8.0

The terminal application is released alongside [GUI v1.8.0](https://github.com/loicdelaunay/OhMyHarness/releases/tag/v1.8.0), using the same shared engine.

## Download

Download `OhMyHarness-CLI-v1.8.0-win-x64.zip`, extract it into a writable folder and run `omh.exe` from a terminal. Use `/connect` to configure a provider. The archive contains only the standalone EXE, MIT license and startup instructions. Verify it with the attached `SHA256SUMS.txt`.

To upgrade, close the CLI and replace only `omh.exe` in its existing portable folder. Keep and back up your database and resources. No conversations, keys or user configuration are included in the download.

## Changes since CLI v1.6.1

- Independent font and size selection through `/font`, with bundled VT323, Share Tech Mono and Space Mono fonts and their original OFL licenses. Font changes use a terminal-host profile.
- `/update` checks GitHub, verifies a compatible download, replaces only the executable and restarts while preserving data.
- Text selection and editing: Ctrl+A, Shift+arrows, Ctrl+arrows, Ctrl+Shift+arrows, Home/End, word deletion, clipboard and undo/redo. Ctrl+C still stops the agent when no text is selected.
- Fixed ordinary Backspace deleting a word on hosts that send DEL; both ordinary encodings now delete one character/grapheme. Ctrl+W deletes the previous word when the terminal does not send distinct modifiers.
- Shared response-style selection in `/settings`: DEFAULT, SHORT, PRAGMATIC, DETAILED and FUN.
- English documentation across `docs/`, with updated keyboard, font, update and settings guides.

See the [CLI guide](https://github.com/loicdelaunay/OhMyHarness/blob/main/docs/cli.md) and [changelog](https://github.com/loicdelaunay/OhMyHarness/blob/main/CHANGELOG.md).

## Requirements and validation

Windows x64 executable with bundled .NET, Python and multilingual embedding resources. A chat provider is required; the chat model is not bundled. Optional Git, Docker/Podman, OpenCode and MCP features retain their prerequisites. Use a Unicode/truecolor terminal.

163 CLI checks and 38 targeted shared-engine checks passed. Both Windows publications succeeded; the CLI executable reports version 1.8.0. No macOS binary or native macOS validation is included.
