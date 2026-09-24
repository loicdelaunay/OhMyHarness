# Search, patches and local reports

Enable **Code search glob/grep** and **Multi-file patch with diff** under Settings → Skills or **+ → Skills**. Both skills work independently of the original editing skill, inside project folders, on Windows and macOS hosts.

- `glob_sources`: filenames and relative patterns such as `**/*.cs`, `src/**`, `*.md` (`*`, `**`, `?`).
- `grep_sources`: literal text or regular expression, glob filter and case option; results use `path:line:text`.
- `patch_sources`: an `edits` list containing `path`, `old_text`, `new_text`. Each old text must match exactly once; add context if it repeats. `old_text: null` creates a new file and never replaces an existing one. Multiple edits to the same file are processed in order.

The patch returns a unified diff. `dry_run` defaults to `true`: no writes occur. With `false`, the application requests approval according to the chosen policy (deny/ask/allow and saved permissions), verifies that files have not changed, then applies the batch. Source-tool writes are serialized and rollback is attempted if a write fails. The batch is not a transaction resilient to abrupt PC shutdown. UTF-8 files, their BOM and line endings are preserved outside replaced text.

Paths outside the project, secrets and symbolic links are excluded. Grep and patch limit files to 128 KB. Searches limit scanned entries, result counts and output size; incomplete results are reported. Narrow the glob to continue a large search.

Recognized paths in answers (for example `docs/report.html`), backtick paths and Markdown links become clickable, including in history. For paths with spaces, use `[Report](<docs/my report.html>)`. Clicking opens a local preview in the embedded browser, with usual permissions, in the conversation's project. Code blocks and web links remain unchanged.

Existing `write_source` and `edit_source` tools remain for compatibility. The new patch tool does not accept a diff string as input: it generates the diff from validated exact replacements.

## Partial reads in Source exploration

`read_source` accepts two optional integer parameters: `start_line` and `end_line`, supplied together. Numbers are 1-based and both endpoints are inclusive.

```json
{"path":"src/app.cs","start_line":40,"end_line":80}
```

The result reports the actual range read and numbers lines, including blank ones. If the end exceeds the file, reading stops at the last line. A start beyond the file is explicitly reported. Without bounds, full reads remain unchanged.

Limits: 2,000 lines and 128,000 characters per excerpt, files up to 16 MiB (versus 128 KB for a full read). Path protections, permissions and sandbox restrictions still apply. Available on Windows, macOS and for subagents using internal tools.
