# Direct HTTP web research

Enable **Web research** in GUI Settings → Skills or CLI `/skills`. The agent can fetch HTTP(S) pages and APIs with the built-in asynchronous .NET client. No browser, MCP server or extra installation is required. GUI and CLI use the same implementation.

## Tools

- `web_http_request`: URL, optional method (`GET`, `HEAD`, `POST`, `PUT`, `PATCH`, `DELETE`, `OPTIONS`), headers as an array of `{ "name": "Accept", "value": "application/json" }`, and optional UTF-8 body (64 KiB maximum). Content headers require a body; GET and HEAD do not accept one.
- `web_http_configure`: inspect settings with no arguments, change selected options, or use `reset: true` to restore defaults and clear cookies.

| Client option | Default | Limit / behavior |
| --- | --- | --- |
| `timeout_seconds` | 30 | 1–120 seconds, including response streaming and redirects |
| `max_response_bytes` | 65536 | 1–1048576 bytes after automatic decompression |
| `follow_redirects` | false | Maximum 5 redirects; permissions checked for each destination |
| `decompress` | true | Automatic gzip, deflate and Brotli support |
| `use_cookies` | false | Isolated cookie jar for this agent run |
| `user_agent` | `OhMyHarness/1.0` | Custom valid User-Agent, maximum 256 characters |
| `proxy_url` | empty | System proxy by default; explicit HTTP(S) proxy origin without credentials |

Settings and connections are reused during the current agent run and disposed at its end. Changing configuration clears the cookie jar. Headers and request bodies are never saved as client defaults. This client does not share provider API keys, browser cookies or operating-system credentials.

Requests use the application's permission flow. A denied request sends no traffic. Plan mode, sandbox mode and delegated subagents cannot use these tools. TLS certificate validation always remains enabled, HTTPS-to-HTTP redirects are blocked, and custom headers are removed on cross-origin redirects. HTTP endpoints can be used explicitly, including local development APIs.

Responses include the URL, status code, headers, body, body encoding, truncation flag, redirect count and elapsed time. HTTP error responses such as 404 retain their body. Binary or undecompressed content is returned as base64; Set-Cookie response values are redacted. Response size is bounded while streaming. Text comes from the HTTP response: scripts are not executed, so JavaScript-rendered sites may require the separate browser skills.

Example request to the agent: “Fetch this API as JSON using direct HTTP. Set a 15-second timeout, keep redirects disabled, and report the response status and useful fields.”
