# Conversations, resources and compatibility

These features are shared by WinUI on Windows and the Electron Windows/macOS interface.

## Resources and projects

**Manage project** configures multiple default folders. A conversation inherits them until its resources are customized. The **+** menu can add files or folders and restore project defaults. Dropping files or folders onto the composer adds them to the conversation's scope. A file attached as a source grants access only to that file, not its parent folder. The Images button continues to send images to the model.

Changes apply to the next send. An active generation keeps its configuration snapshot. Git, the explorer, new terminals, source tools and RAG results respect conversation resources. An existing terminal in a detached folder can no longer launch commands. The sandbox retains its folder-copy constraints and requires a new conversation when its roots change.

`AGENTS.md`, `Agent.md` and `AGENT.md` load automatically from attached folders and subfolders. Instructions remain subordinate to the user's request, Plan mode and permissions.

## permission.json

Place this file in a default project folder, then use **Manage project → Read/Import permission.json**. Review the rules and save/apply them. The application keeps a snapshot in SQLite: editing the file, including by an agent, does not change permissions without a new import. New generations use that snapshot.

```json
{
  "permissions": {
    "terminal": "ask",
    "python": "ask",
    "desktop": "deny",
    "rag-api": "allow",
    "source-patch": "ask",
    "write_source": "deny",
    "edit_source": "deny"
  }
}
```

- Values: `allow`, `ask`, `deny`.
- A key can name a tool, a request family (`terminal`, `python`, `desktop`, `rag-api`, `source-patch`) or an exact scope such as `desktop|mouse`. `*` is the default rule.
- A denial by tool name is checked before execution, including in OhMyHarness subagents. Allowing a tool name does not bypass internal checks, a disabled skill, Plan mode or the sandbox.
- For additional requests: exact scope, then family, then `*`. Global **Deny all** retains priority; `deny` remains a denial even with automatic approval. `ask` requires a dialog even if a permanent grant exists.
- If multiple files define the same key, `deny` wins. Maximum size: 32 KB per file. Removing rules restores the usual policy.
- Tools run by the OpenCode server remain subject to its rules; requests relayed to OhMyHarness pass through the project profile.

## Resume and fork

Hovering over a user message or completed final answer reveals **Message actions**, with **Create fork** and **Resume here**. A fork creates a new conversation through the selected message, including images, conversation settings and resources. Answers containing intermediate tool calls cannot be starting points.

Resuming requires stopping generation and confirmation: full history is first copied into a **Backup** conversation, then the current conversation is rewound to the chosen message. Its queue and OpenCode binding are reset. Previously compacted messages become usable in the restored context. Send another instruction to continue. This does not restore project files or browser/terminal state.

## Display

[Persistent memory](memory.md) has a Settings tab: two skill levels (conversation and shared), Project/General/User categories and indexed SQLite search.

- Conversations show a skeleton during background reads, then the 24 latest messages. Earlier pages load while preserving reading position. Full history remains in the database and is used by the model; only images from displayed pages load into the interface.
- Switching conversations cancels the previous visual load without stopping agents. Drafts are retained. Input and navigation remain available; sending waits until the selected conversation is ready.
- Settings opens immediately with a loading indicator. Git and Files have their own progress bar; an old result cannot replace the new conversation's result.
- The collapsed TODO shows `current step / total · step name`. **×** hides it for the conversation; **+ → Show task list** reopens it.
- Information, tasks and queue use consistent surfaces. Queued messages retain **Delete / Edit / Steer**.
- Action status has a subtle glow. A right-pointing arrow means collapsed; a downward arrow means expanded.
- Git offers **Before / after** and **Combined diff** for the same selected file, from HEAD to the working directory.
- In **General**, below reasoning: **Open and focus the latest AI tool**. Disabled by default; applies only to the visible conversation.

## HTTP and provider errors

OpenAI v1-compatible providers accept `http://` and `https://` URLs, including remote HTTP servers. Credentials embedded in URLs are still rejected. HTTP transmits without TLS encryption; use HTTPS when the server offers it.

API errors show provider details with the request key masked. Some HTTP 400/422 errors identifying an incompatible capability trigger an adapted retry: `reasoning_effort`, streaming statistics, images, tools or DeepSeek reasoning history. Maximum four retries after the initial request. Authentication and unrecognized errors are not automatically retried.

Degraded mode is visible and saved with the answer. Original history and images remain in the database. If tools are refused, the response becomes text-only and cannot act. If vision is unavailable, an explicit marker replaces the image in the request; enable/configure **Bypass image AI** and disable the main model's image capability to delegate analysis. No other paid provider is selected automatically.

The mechanism does not replay a request after a successful stream has started, avoiding duplicate actions. HTTP 400 alone does not establish that a key or a particular capability is responsible.
