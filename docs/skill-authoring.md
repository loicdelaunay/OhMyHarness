# Automatic skill creation

Enable **Settings → Skills → Automatic skill creation** to give the agent the `skill_locations` and `create_skill` tools. The first lists available locations. The second creates a folder named after the skill and writes a `SKILL.md` with `name`, `description`, `created_utc` and Markdown instructions. Created skills are enabled and loaded on demand with `load_skill`.

The `global` scope writes to the portable `skills` folder beside the executable. The `project` scope writes to `<source folder>/.omh-ai/skills`. If multiple source folders are attached, the agent selects an alias returned by `skill_locations`. Each location holds at most 20 skills. Creating the 21st deletes the oldest. The permission request shows the proposed content and the folder that will be deleted.

Plan mode prohibits `create_skill`. Sandbox mode does not expose this tool. `SKILL.md` files remain subordinate to application permissions and modes. Automatic skill creation tools are not exposed to native OpenCode sessions; OpenAI-compatible and DeepSeek models can use them.
