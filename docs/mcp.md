# MCP and automatic continuation

The WinUI Windows and Electron Windows/macOS interfaces provide **Settings → MCP**, immediately after Skills, and **+ → MCP** to enable or disable each server.

## Add a server

Choose **Add MCP server**, enter a name and select the transport:

- `stdio`: executable (`node`, `npx`, `uvx`, absolute path…), arguments as a JSON array, optional working directory. The executable must be installed. On Windows, for a `.cmd` launcher requiring a shell, explicitly use `cmd.exe` with its arguments; on Mac, use the Unix launcher or its absolute path.
- `http`: MCP Streamable HTTP URL, for example `https://server.example/mcp`.
- `sse`: SSE endpoint for older servers. Use Streamable HTTP for newer servers.

HTTPS is required remotely; HTTP is accepted on localhost. Enter optional secrets in **Secrets JSON**, for example:

```json
{
  "environment": { "MY_API_KEY": "value" },
  "headers": { "Authorization": "Bearer value" }
}
```

`environment` applies to stdio, `headers` to HTTP/SSE. Leaving this field empty during editing keeps stored secrets; the dedicated checkbox clears them. They are encrypted with DPAPI on Windows or the macOS Keychain. Do not put secrets in arguments or URLs, which remain ordinary configuration fields. Local servers receive standard system variables and configured values, not every secret in the application's environment.

In WinUI, **Test MCP connection** uses the draft; **Save** applies all changes. In Electron, save the server and then click **Test connection**. Results list the server's advertised tools. Configuration does not automatically install server dependencies; a launcher such as `npx -y …` may do so when run after approval.

## Agent use

Tools from enabled servers are added to **OpenAI-compatible and DeepSeek** provider calls. Each server has a separate namespace to prevent collisions between identically named tools. Text/structured results are sent to the model; a PNG/JPEG/WebP image up to 8 MB may also be attached if the model accepts images. MCP resources and prompts do not yet have a dedicated explorer, and interactive OAuth authentication is not implemented: use headers or variables supplied by the server.

**OpenCode keeps its own MCP servers and tool loop.** Servers configured here are not copied into its configuration. The continuation option concerns the twelve-call limit imposed by OhMyHarness on Chat Completions-compatible providers.

Connections and tool execution respect **Ask / Deny all / Automatic approval**. “Always allow” remains scoped to the server and, for an action, to the relevant tool; changing command, URL, arguments or secrets invalidates the previous scope. Approvals remain revocable in Settings.

Settings and toggles are global. Disabling a server blocks subsequent calls, including one still awaiting approval; it does not undo an operation already executed. **Stop** cancels the conversation and closes its connections. Each conversation owns its stdio connections, so multiple conversations may launch multiple instances of the same server. Denied or failed connections are not retried in a loop; fix the configuration or send a new message to retry.

## Automatic continuation

**Settings → General → Automatically continue after 12 steps** is disabled by default. When enabled, the agent retains its history and continues calls beyond the twelfth step. It stops when it produces a final answer, encounters a blocking error or receives **Stop**. The option does not approve permissions for the user. Disabling it restores stopping at the next twelve-step boundary.

Configuration is stored in `database.sqlite`, with an EF Core migration preserving existing data. Automatic continuation may consume more tokens. MCP operations are limited to 30 seconds for connection and two minutes for a tool call.

## Validation

Local automated tests: migrations/persistence, simulated stdio and HTTP MCP servers, header authentication, environment transmission, denial before launch, disabling before execution, cancellation during a tool, twelve-call limit and fourteen calls with continuation enabled. Electron UI tests: tab placement, creation/editing and the + menu toggle. Third-party servers and native Mac execution require their own testing.

The integration uses the [official MCP C# SDK](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/transports/transports.html), version 2.2.0.
