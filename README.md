<p align="center">
  <img src="src/OhMyHarness.App/Assets/logo-256.png" alt="OhMyHarness logo" width="112">
</p>

<h1 align="center">OhMyHarness</h1>

<p align="center"><strong>Your AI workspace in one portable desktop app.</strong></p>

<p align="center">
  <a href="https://github.com/loicdelaunay/OhMyHarness/releases/latest">Download for Windows</a>
  · <a href="#quick-start">Quick start</a>
  · <a href="#build-from-source">Build from source</a>
  · <a href="docs/guide-fr.md">Français</a>
</p>

**Tired of powerful AI harnesses that need a stack of configuration and external services before the first conversation? What if a portable EXE handled the workspace?**

OhMyHarness brings projects, concurrent chats, agents, sources, tools, and settings into one application. The Windows release ships as a self-contained executable. Put it in a writable folder, connect a model provider, and start working. Its SQLite database and application-managed resources live beside the EXE, so you can move the workspace by copying the folder after closing the app.

The chat model itself is **not** bundled: cloud providers need network access and, depending on the service, an API key. Git, Docker/Podman, OpenCode, and Chrome MCP are optional integrations with their own prerequisites. The core app does not require a separate OhMyHarness server.

## Take a look

![OhMyHarness project conversations and chat interface, captured with synthetic demo data](docs/images/readme/chat.png)

<sub>Real application capture with synthetic demo data. Project chats, model choice, response speed, context usage, and the composer stay in view.</sub>

<table>
  <tr>
    <td width="50%">
      <a href="docs/images/readme/workflows.png"><img src="docs/images/readme/workflows.png" alt="Tools and work modes menu" width="100%"></a><br>
      <sub>Attach sources, select Plan or Execution, configure subagents, and toggle skills.</sub>
    </td>
    <td width="50%">
      <a href="docs/images/readme/settings.png"><img src="docs/images/readme/settings.png" alt="Application settings" width="100%"></a><br>
      <sub>Language, themes, providers, permissions, MCP, and more in one settings window.</sub>
    </td>
  </tr>
</table>

<details>
<summary>See the two-level memory settings</summary>

![Conversation and shared memory settings](docs/images/readme/memory.png)

</details>

## What is inside

| Area | Capabilities |
| --- | --- |
| **Models and providers** | Add as many OpenAI-compatible v1 or DeepSeek connections as you need, select their available models, connect OpenCode, or build a composed model with an orchestrator and specialized subagents. |
| **Projects and conversations** | Attach source folders to a project, run several chats at once, queue or steer messages while an agent works, fork or resume from an earlier message, and export a conversation to Markdown. |
| **Agent tools** | An integrated browser with controlled DOM and JavaScript access, multiple asynchronous terminals, source search and editing, a read-only Git diff viewer, file navigation, screenshots, mouse and keyboard controls, and bundled Python for scripts. |
| **Control and extensions** | Per-conversation Plan and Execution modes, optional container sandbox, scoped approval dialogs, configurable skills, project instructions from AGENTS.md, custom SKILL.md files, and MCP servers. |
| **Context that lasts** | Live token and speed indicators, context compaction, conversation and shared memory, semantic search with a bundled multilingual embedding model or an OpenAI-compatible embedding API, and an optional vision-model bridge. |
| **Desktop workflow** | Scheduled project tasks, structured agent checklists and questions, English/French UI, light/dark themes, custom app name and logo, and keyboard-friendly chat. |

Plan mode blocks modifying tools at the application boundary. The optional sandbox uses Docker or Podman and keeps a copy of approved sources separate from the real project until changes are reviewed. Permissions still apply to sensitive actions. See the [agent modes](docs/agent-modes.md) and [sandbox guide](docs/sandbox.md) for their exact boundaries.

## Quick start

1. Download the [latest Windows x64 release](https://github.com/loicdelaunay/OhMyHarness/releases/latest).
2. Extract the archive into a **writable folder** and run <code>OhMyHarness.App.exe</code>. Keep the included <code>skills/</code> folder beside it.
3. Open **Settings → Providers**. Add a provider and its API key or endpoint. **Test connection** detects, selects, and saves its models; you can change that selection later.
4. Create a project, attach the source folders you want to share with its chats, and start a conversation.

The integrated browser uses the system Web engine: Edge WebView2 on Windows and WebKit on macOS. Git, container sandboxing, OpenCode, and Chrome MCP only need installation when you enable those integrations. Scheduled tasks run while OhMyHarness is open; they do not wake a sleeping computer.

## Portable data and privacy

OhMyHarness stores <code>database.sqlite</code>, <code>skills/</code>, <code>MCP.json</code>, browser profiles, and other application-managed resources beside the executable. EF Core applies database migrations on startup. Do not place the app in a read-only directory such as <code>Program Files</code>. Close all instances before copying the folder to another machine.

API keys use Windows DPAPI or the macOS Keychain. They are tied to the OS account, so moving the folder to another user or operating system requires entering the keys again. The rest of SQLite is **not encrypted**. Only content used for a request is sent to its selected model provider; attaching a source folder does not upload the whole folder automatically.

The bundled RAG embedding model runs locally and supports French and English. Its model file is part of the app release. Source clones use **Git LFS** to retrieve that file.

## Platforms and building

The shared application is built with **Uno Platform and .NET 10**. The published Windows x64 release uses Uno Desktop; a native WinUI 3 target is also available. The macOS Uno Desktop target is in the source tree, but native interactions still need validation on a real Mac. There is no signed macOS binary in the current release.

For a Windows source build, install the .NET 10 SDK and Git LFS. The native WinUI target additionally needs the Windows/WinUI development tools.

~~~powershell
git clone https://github.com/loicdelaunay/OhMyHarness.git
cd OhMyHarness
git lfs pull
dotnet run --project tests/OhMyHarness.Tests -c Release
.\publish.ps1 -OutputDirectory artifacts\release\win-x64
~~~

The publication above leaves <code>artifacts\official</code> untouched. Use <code>.\publish.ps1 -NativeWinUI -OutputDirectory artifacts\release\winui</code> for the native WinUI target. On a Mac with the .NET 10 SDK and Xcode command-line tools, use <code>bash ./publish-macos.sh arm64</code> or <code>x64</code>; signing and notarization are separate steps.

The [Windows v1.0.0 release](https://github.com/loicdelaunay/OhMyHarness/releases/tag/v1.0.0) passed 473 offline .NET checks and an application UI smoke scenario. The macOS target is not included in that runtime validation.

## More documentation

The detailed guides are currently mostly in French: [browser, RAG, and subagents](docs/browser-rag-agents.md), [memory](docs/memory.md), [custom skills](docs/skill-authoring.md), [MCP](docs/mcp.md), [scheduled tasks and models](docs/uno-tasks-models.md), [macOS](docs/macos.md), and the [full French guide](docs/guide-fr.md).

OhMyHarness is available under the [MIT license](LICENSE).
