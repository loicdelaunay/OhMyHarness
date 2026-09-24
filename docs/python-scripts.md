# Python Script skill

Enable **Python Script** under **Settings → Skills** or in the **+** menu.
It also works without an attached source folder.

- `python_info`: version, embedded interpreter path, extraction status and conversation scripts.
- `write_python_script`: create or replace a `.py` script after approval.
- `run_python_script`: execute a saved script with literal arguments and return `exit_code`, `stdout`, `stderr`, `timed_out`.

```json
{"path":"report.py","code":"from pathlib import Path\nPath('report.txt').write_text('Hello!', encoding='utf-8')\nprint('Report created')"}
```

Then, using `run_python_script`:

```json
{"path":"report.py","args":[],"timeout_seconds":30}
```

The working directory is the project's first source folder, or the conversation's
scripts folder if no sources are attached. `working_directory` can select another
attached source folder. Imports between scripts are supported. Python isolated
mode ignores `PYTHONHOME`, `PYTHONPATH` and the PC's user packages; it is **not a
security sandbox**. Scripts run with the user's rights and can access the system
after approval.

Requests respect Deny all / Ask / Automatic approval and saved grants. In Plan,
only the `python_info` query is allowed. These local tools are removed in container
sandbox mode, with no local fallback. Execution does not accept interactive input.
Default timeout: **30 seconds**, maximum **600 seconds**. Cancelling the conversation
stops execution; each output stream is limited to 100,000 characters. Do not detach
background processes.

## Portable publishing

**CPython 3.13.15**, from [python-build-standalone](https://github.com/astral-sh/python-build-standalone/releases/tag/20260901),
is embedded as a compressed program resource. Downloading happens on the build
machine, with SHA-256 pinned in `build/PythonRuntime.targets`. No download or
installed Python is required on the machine running the application.

WinUI/Service builds and publishes import this target automatically, including a
direct `dotnet publish`. Architectures: Windows x64/ARM64, macOS Intel/Apple Silicon.
The first build requires Internet; later builds can reuse the verified archive in
`artifacts/python-cache`.

On the first approved script launch, the runtime is extracted into the portable
profile beside the Windows EXE (or the profile supplied by the Electron host):

```text
OhMyHarness.App.exe
database.sqlite
skills/
runtimes/python/3.13.15-20260901-win-x64/python/...
scripts/python/chat-41/report.py
temp/python/chat-41/...
```

The standard library, native extensions and distribution licenses are retained.
Specialized libraries such as NumPy/Pandas are not preinstalled. The Windows x64
archive adds about 47 MB to the publication; the extracted runtime occupies more
space. Updates do not automatically delete older extracted versions.

Portable validation: publish `tests/PythonRuntime.Probe` as a single EXE, then run
it from another directory. This test uses the actual embedded interpreter and
checks permissions, script scope, imports, Unicode, SQLite/SSL, errors and timeout.
