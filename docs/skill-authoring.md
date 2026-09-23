# Auto-création de skills

Activez **Réglages → Skills → Auto-création de skills** pour donner à l'agent les outils `skill_locations` et `create_skill`. Le premier liste les emplacements possibles. Le second crée un dossier nommé d'après le skill et y écrit un `SKILL.md` avec `name`, `description`, `created_utc` et les instructions Markdown. Les skills créés sont activés et chargés à la demande par `load_skill`.

Le scope `global` écrit dans le dossier portable `skills` à côté de l'exécutable. Le scope `project` écrit dans `<dossier source>/.omh-ai/skills`. Si plusieurs dossiers sources sont associés, l'agent choisit l'alias donné par `skill_locations`. Chaque emplacement contient au maximum 20 skills. Lors de la création du 21ᵉ, le plus ancien est supprimé. La demande d'autorisation affiche le contenu proposé et le dossier qui sera supprimé.

Le mode Plan interdit `create_skill`. Le mode sandbox ne donne pas accès à cet outil. Les fichiers `SKILL.md` restent subordonnés aux autorisations et aux modes de l'application. Les outils d'auto-création ne sont pas exposés aux sessions OpenCode natives ; les modèles OpenAI compatibles et DeepSeek peuvent les utiliser.
