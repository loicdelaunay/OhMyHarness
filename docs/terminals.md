# Multiple terminals

The **Terminal** panel contains multiple tabs scoped to each conversation. **+** opens a local terminal and **×** closes it, stopping its command. Each tab retains its name, state, draft and last command output during the application session. Switching conversations or opening Settings does not stop commands.

Local output updates during execution. Agent sandbox terminals appear in the same panel with a **Sandbox** label; output becomes available when the container returns. They can be stopped or closed from the panel. Only the sandbox agent can launch them, to preserve its isolated workspace.

## Terminal skill tools

| Tool | Purpose |
|---|---|
| `list_terminals` | List terminals in the conversation and current mode, their shell, state and latest output. |
| `create_terminal` | Create a named tab in the project source folder. |
| `start_terminal` | Request permission, launch a command and immediately return its `jobId`. |
| `read_terminal` | Read available output without waiting. |
| `wait_terminal` | Wait asynchronously up to `timeout_ms`, then return state and output; 10 seconds by default, 30 seconds maximum. |
| `stop_terminal` | Stop the command without closing the tab. |
| `delete_terminal` | Stop the command and remove the tab. |

Tools use `terminal_id`. To read or wait for a particular command, also provide `job_id` using the `jobId` returned at launch. The last ten commands remain accessible by identifier. `run_terminal` remains for compatibility: it also uses a tab but waits for its command.

The agent can start a command in A, start another in B, then inspect or wait for their results. Waiting does not hold the global tool lock. Waits of at least one second do not trigger identical-call protection; repeated immediate polls remain checked.

## Limits and scope

- Commands are **non-interactive**, with a new PowerShell process on Windows or zsh on macOS. They are not a persistent shell: variables, `cd` and interactive sessions do not survive between commands. Group related operations in one command.
- One active command per tab, 12 tabs per conversation, 64 overall and 16 simultaneous commands. The original implementation limited each command to 60 seconds with output capped at about 100,000 characters; configurable command deadlines are described in [the guide](guide.md).
- `completed` means the process exited; its exit code appears in the result. `failed`, `cancelled` and `timed_out` indicate other outcomes.
- Permissions are requested at launch according to the global setting. Plan mode may list, read and wait; it cannot launch commands.
- An agent cannot manipulate another conversation's terminal or use a local terminal from a sandbox. Terminals pointing at a detached folder can no longer launch commands.
- In the sandbox, parallel commands use separate copies. Only modified files are merged back into the conversation copy after checking original contents. Conflicts are reported and the recovery copy is retained. Applying to the real project still requires sandbox review. Sandbox commands stop at generation completion before the isolated workspace is released.
- Closing the application or deleting a conversation stops its commands. Tabs are not restored after restarting.
