# Tasks, questions and loop protection

Available in WinUI on Windows and the Electron interface on Windows/macOS.

## Task list

The agent uses `todowrite` to replace the conversation's structured list. Statuses are **Pending**, **In progress**, **Completed** and **Cancelled**. The card updates and shows the number of completed tasks. It can be collapsed. The list is saved in `database.sqlite`, restored after reopening and included in context on the next send. The agent decides when to create and update steps; the application does not automatically turn every textual plan into tasks.

The list uses a `Message` row with `Role=tasks`, `State=ui`, separate from provider history. No SQLite schema change is required. `todowrite` results remain in normal tool history. Direct subagents do not replace the parent's list.

## Interactive questions

The `question` tool offers 1–8 questions with single/multiple choices and/or free text. Answers are sent only after form submission. A selection is never made on the user's behalf. Plan mode allows questions and task lists.

Only the agent awaiting an answer pauses: other conversations and subagents can continue. Forms stay in their conversation without blocking Settings. Switching conversations preserves answers being entered. Answers and cancellations are saved in SQLite; stopping generation removes its pending questions. A cancelled form explicitly returns cancellation to the model, never approval. Unsubmitted forms are abandoned when the application closes, like active generations.

Questions are not access permissions: **Deny all**, **Ask**, **Automatic approval** and saved grants do not answer these forms. Privileged tools retain their own checks.

## Repeated calls

Before the **third consecutive call** to the same tool with identical arguments, the application pauses that work and offers **Stop** or **Continue once**. JSON property order and formatting whitespace do not evade detection. A different call resets the sequence. Continuing authorizes only that call; another identical call asks again. The sequence survives compaction and automatic continuation and is isolated per conversation/subagent. This is not semantic detection of every possible loop (such as alternating between two tools).

## OpenCode

With OpenCode tools enabled, questions are relayed from `/question` and tasks from `/session/{id}/todo`. Only requests from the relevant session are processed. Answers use `reply` or `reject`. The session receives an explicit `doom_loop: ask` rule; those decisions use the form even with automatic approval. Stopping this loop interrupts the remote session. The relay waits for the session's idle state to avoid confusing the end of an intermediate model call with completion of the work.

The server must support these routes and session-permission updates. A relay error interrupts monitoring and requests session shutdown; it is not treated as implicit consent. OpenCode itself detects repeated native calls. Consultation sessions created by the application have the same question relay without replacing the parent's task list. Native subagents created by OpenCode retain its own management.

References: [OpenCode tools](https://opencode.ai/docs/tools/), [OpenCode permissions](https://opencode.ai/docs/permissions/).
