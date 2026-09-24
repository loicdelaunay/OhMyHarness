# Browser, RAG and subagents

## Browser

Opening Tools or switching tabs no longer starts the browser. In Web, use the navigation arrow to start WebView2. A creation error or process-failure event displays an error limited to the browser; other tools and conversations remain available. Pending navigation completes with an error when the process stops.

Under Settings → Browser, choose embedded WebView2, Chrome MCP or Disabled. Chrome MCP exposes Chrome DevTools tools to the agent in an external window; WebView2 tools are removed from its catalog. Chrome and Node.js/npm must be installed. A custom Chrome path is optional. The first connection asks for MCP approval and may download the npm package. MCP tool permissions and Plan mode remain enforced. Chrome profiles are separated by conversation in `Chrome/chat-<id>` beside the executable. MCP closes at generation completion; its profile is retained. Do not manually launch two instances against the same profile.

This handling uses [WebView2 process events](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/process-related-events) and [Chrome DevTools MCP](https://github.com/ChromeDevTools/chrome-devtools-mcp). It cannot prevent security software from terminating OhMyHarness itself. No antivirus-policy bypass is installed.

## Sources and Markdown

`read_source` accepts every extension and extensionless files. UTF text is read normally with existing excerpt limits. Binaries are shown as hexadecimal: one line represents 16 bytes, and `start_line`/`end_line` allow continued reading. This is not a PDF, Office or OCR text extractor. Outside approved sources, reading/transmission still requires approval. Automatic-exploration, write and sandbox exclusions remain enforced.

**↓ Auto** follows the conversation. Scrolling upward disables it; returning to the bottom re-enables it. Triple-backtick Markdown blocks receive syntax highlighting while retaining copyable text. Highlighting distinguishes keywords, strings, numbers and comments; very large blocks stay plain text to preserve responsiveness.

## RAG

Under **Settings → Skills**, enable **Semantic RAG search**: its settings appear directly beneath the skill. The panel is hidden when the skill is disabled, retaining its configuration.

- **Local**: quantized multilingual MiniLM L12-v2, CPU, about 118 MB of weights embedded in the EXE. It supports French and English, including French questions over English sources and vice versa. Resources extract to `models/minilm_multilingual` beside the data on first use. No runtime download or API call. The tokenizer preserves case and French accents.
- **OpenAI v1 API**: select a saved provider and an embedding-model name (different from a chat model). Its encrypted key is reused. Indexed texts and queries are transmitted only after approval; responses are normalized for similarity search.

The agent has `rag_index` (rebuild), `rag_search` (semantic search), `rag_sources` (browse indexed paths) and `rag_read` (read current lines from a result). The index is stored in SQLite by project and model with file fingerprints. Rebuilding is atomic; files changed since indexing are excluded from results until the next rebuild. Each result contains a path, line range, excerpt and similarity score.

Every extension can be indexed: UTF-8/UTF-16 and Windows-1252 text, PDF text, Office/OpenDocument/EPUB. Unknown archives produce an inventory; unknown binaries produce metadata and printable strings explicitly marked as partial extraction. Images, videos, audio, encrypted documents and scanned PDFs are not visually interpreted by this extractor; use the vision skill for an image. `rag_read` reads extracted text lines for PDF/Office, not line numbers from the original binary format.

Limits: 1–2,000 files depending on settings (500 default), 5,000 passages, 32 MiB and 500,000 extracted characters per file, 1–20 results (5 default). Above the size limit, only metadata is indexed. Source exclusions remain enforced. Very long passages are truncated for embedding; use `rag_read` for context. Indexing may take time and consume API calls. Results and catalog are filtered by conversation resources; rebuilding those resources does not remove passages from other project sources. Rebuilding is unavailable in Plan mode; RAG is unavailable in the sandbox.

After upgrading the old English local model, ask the agent to **reindex sources with `rag_index`**. Old vectors are ignored to prevent incompatible comparisons, then replaced on rebuild. API indexes are unchanged.

Model: [paraphrase-multilingual-MiniLM-L12-v2](https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2), Xenova quantized ONNX export, Apache-2.0 license included with resources. Mean pooling, L2 normalization, 384 dimensions, 128-token window. Engine: Microsoft.ML.OnnxRuntime 1.30.0; SentencePiece tokenizer: Microsoft.ML.Tokenizers 2.0.0.

## Subagents

Subagents created by the OhMyHarness orchestrator appear as chat bubbles and, while working, as children of the conversation in the left sidebar. Clicking opens their task, activity and exchanges. Back returns to the parent. Parent input is disabled in this view to avoid sending to the wrong recipient; parent generation continues.

On completion, the sidebar entry disappears and the bubble remains accessible. Exchanges are stored in SQLite and reloaded with the conversation. Work still marked active after a restart is shown as interrupted. Subagents internal to the OpenCode server are not exposed in this view.

## Settings and composite models

Settings uses categories on the left and scrollable content on the right. On Windows, it stays in a window independent of running agents.

Under **Providers → + Composite model**, choose a saved provider and model for the orchestrator, then add 1–6 subagents. Each has a unique name, provider, model and task. **Load models** lists the provider's models. Composite models can be edited, duplicated, deleted and selected in the usual provider picker.

At the start of each turn, configured tasks run in parallel with the user's request; the orchestrator receives their results and continues. Selecting a composition implies Forced orchestration. Keys belong to referenced providers; they are not copied into the composition. Active generations keep their configuration if settings change.

Subagents retain OhMyHarness orchestrator limits: 8 steps each, 6 total per turn, inherited source access and permissions, no terminal/MCP/browser or recursion. OpenCode subagents remain read-only/in Plan. OpenCode, including as a subagent, stays unavailable in the sandbox. Choose independent tasks to avoid writes to the same files.

## Messages during generation

Input and Send remain available. Sending during generation queues the message by default. Each row offers Delete, Edit and Steer:

- **Queue** (default): start a new turn after current work, using the provider selected when sent.
- **Current execution**: inject an instruction and its images at the next step using the current run's model. An in-flight HTTP request cannot be changed. Insertion waits for the response and its tool calls to finish to retain valid history. With OpenCode, it must wait for the current response to finish.

Queued messages appear above input, can be removed and are persisted per conversation in SQLite. After an error, deliberate stop or restart, **Resume queue** relaunches remaining messages. They do not run automatically at startup. An instruction leaves the queue only when its user message is saved. New messages in other conversations remain independent.

## Settings, compositions and send validation

Service tests with a simulated local API: steering/queue order, stop/resume, conversation isolation, model/key routing, reports sent to the orchestrator and subagent Plan mode. SQLite tests: data reopening, image transfer and single consumption. Real Electron test: side navigation, conditional RAG panel, composition creation/editing and input availability during generation.

## Tool validation

.NET tests: arbitrary formats, binary reads, highlighting and HTML escaping, migrations, persisted subagents, actual multilingual MiniLM, French tokenizer conformity to the reference, French/French and French/English search in both directions, old-index replacement and stale-file exclusion. Service tests: embeddings through a simulated OpenAI v1 server, no transmission after denial. Electron test: opening Tools without the browser, Auto, subagent navigation and an interface that stays responsive after deliberately stopping the browser renderer. Optional Chrome test: `dotnet run --project tests/OhMyHarness.Tests -c Release -- --chrome-smoke` uses an independent profile and Chrome without a visible window.

Corporate antivirus policy and the macOS interface require checks on their respective machines.
