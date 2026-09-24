# Development sandbox (Windows and macOS)

Enable **+ → Sandbox** for subsequent sends in this conversation. The **ⓘ** button explains its behavior in French or English. The preference is stored in SQLite with automatic migration. An active send keeps its initial mode.

## Prerequisites

Install and start **Docker Desktop** in Linux-container mode, or **Podman** with its Linux machine. Download the development image before use:

```sh
docker pull node:22-bookworm
# or
podman pull node:22-bookworm
```

The application prefers Docker, then Podman. It does not download images automatically and **never** falls back to the local terminal if the engine is unavailable. OpenAI-compatible and DeepSeek APIs are still called by the application outside the container.

## How it works

- A separate persistent source copy per conversation, under `sandboxes/` beside SQLite. The initial copy includes uncommitted changes. It does not automatically follow later changes to the original.
- Read, search, write and patch tools, plus subagents, use this copy. Outside paths remain prohibited even with global permissions set to “allow all”.
- Each terminal command starts an ephemeral container. Sources arrive as an archive; **no host folder, Docker socket, API key or SQLite mounts**. Only validated regular source files return to the copy. Links, path traversal, devices and oversized archives are rejected.
- Networking is technically disabled (`--network=none`). There is not yet an option to allow selected domains. Required dependencies must already be in the image; Internet installation and persistent servers are unavailable.
- Read-only root filesystem, all Linux capabilities dropped, `no-new-privileges`, commands run as UID 1000. The supervisor uses another unprivileged UID so a command cannot suspend its automatic shutdown.
- 1 CPU, 512 MiB RAM, 128 processes; temporary volumes of 128 MiB and 64 MiB. 60 seconds per command, 30 minutes per generation. Stop destroys the container, including background processes. A 120-second lifetime provides extra protection if the application closes abruptly.
- Sources are limited to 64 MiB, 10,000 files, 2 MiB per file. Filters exclude `.git`, dependencies, build outputs, known secrets and disallowed formats. **A secret embedded in a source file is not detected automatically.**

## Review and apply

After generation, use **+ → Review sandbox changes…**. The review shows creations, modifications and deletions relative to the initial copy. For binaries, it shows change type and size. Explicitly click **Apply these changes** to write to the original project. This step does not use automatic permissions. Original files are rechecked byte for byte; an external change causes the batch to be rejected.

Originals stay untouched if you close the review. The copy remains available to continue the conversation. Disabling Sandbox returns to local mode on the next send without deleting the copy. If project source folders change, create a new conversation to start another copy.

## Scope of this first version

**OpenCode, MCP, browser and desktop control are blocked in the sandbox**: isolated execution has not yet been integrated. Enabling their skills does not bypass this restriction. Manual tools in the right panel remain local; they are not the sandbox agent's tools.

`git_changes` shows the private copy's diff. Git history and repository configuration are not copied; a terminal may initialize a disposable repository inside its container. Only sources persist between commands, not `.git`, dependencies or background servers. Linux containers cannot build WinUI or use native macOS SDKs.

Limits use documented [Docker mechanisms](https://docs.docker.com/engine/containers/run/). Protection is that of the container engine and its VM; this is not a desktop VM with mouse/keyboard access.

Local validation: copy tests, hostile archives, conflicts, review/apply, migration and UI. A working Docker/Podman engine is required to validate the actual cycle on each OS.
