# Persistent memory

**Settings → Memory** enables two independent skill levels, also available under **Skills** and the **+** menu:

- **Conversation**: information private to the current thread, retained after restarts and context compaction.
- **Shared**: information reusable across conversations according to its category.

Categories are **Project**, **General** and **User**. Shared Project memory is limited to the current project. Shared General and User memory is accessible across projects. Conversation memory stays private to that thread regardless of category. Disabling a skill preserves data but removes model access.

**View memory** opens a list with project, conversation, scope and category filters. Search covers keys, titles, content and tags. Pages contain 20 results; **Show more** loads the next page. Clicking displays full content, version, last modification and originating thread. Manual creation, editing and deletion are saved immediately; the general **Cancel** button does not undo them. Skill toggles are committed with **Save**.

## Model tools

| Tool | Purpose |
| --- | --- |
| `memory_search` | Accent-insensitive word/prefix search; empty query for recent entries. Filters, pagination, excerpts and versions. |
| `memory_read` | Full content and provenance for an accessible ID. |
| `memory_save` | Create using a stable key or update using `id` and `expected_version`. |
| `memory_delete` | Delete using `id` and `expected_version`. |

Creation example:

```json
{"scope":"shared","category":"project","key":"validation.build","title":"Project validation","content":"Run dotnet build before proposing a change.","tags":"build validation"}
```

Model writes use application permissions (ask, deny, automatic approval and permanent grants). In **Plan mode**, only read tools are allowed. Subagents use their parent conversation's memory with the same limits; two writes against the same version cannot silently overwrite each other. Scope, category and key are immutable during updates.

Tools are integrated into the native OpenAI v1/DeepSeek runtime and portable host. The OpenCode server retains its own catalog: application memory tools are not currently exposed to it. In the sandbox, access goes through the application-controlled memory service; the SQLite file is not mounted in the container.

## Storage and search

Everything is stored in **database.sqlite**, beside the executable: the EF Core `Memories` table and SQLite FTS5 `MemorySearch` index. Migration preserves existing data. Triggers automatically synchronize the index on creation, updates, deletion and cascades.

Search combines literal words with AND, accepts prefixes and neutralizes FTS operators supplied by the model. It ranks matches using BM25, favoring titles, keys and tags. Scope filters apply before pagination and on every read or write by ID. This is local lexical search, without embeddings or an external service.

Search returns short excerpts; full content is limited to 16,000 characters per entry. The entire memory is not automatically injected into context: the model searches, then reads relevant entries. Skill instructions ask it to retain useful facts, avoid secrets and duplicates, and treat memories as data to verify.

Deleting a conversation removes its private memory. Deleting a project removes its project memories. Shared General/User memories remain available, with an empty origin if the originating thread was deleted. A fork does not automatically copy the original thread's private memory.
