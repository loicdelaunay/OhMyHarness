# Recherche, patchs et rapports locaux

Activez **Recherche de code glob/grep** et **Patch multi-fichiers avec diff** dans Réglages → Skills, ou dans le menu **+ → Skills**. Les deux skills fonctionnent indépendamment de l'ancien skill d'édition, dans les dossiers associés au projet, sur les hôtes Windows et macOS.

- `glob_sources` : noms de fichiers, motifs relatifs `**/*.cs`, `src/**`, `*.md` (`*`, `**`, `?`).
- `grep_sources` : texte littéral ou expression régulière, filtre glob et option de casse ; résultats `chemin:ligne:texte`.
- `patch_sources` : liste `edits` contenant `path`, `old_text`, `new_text`. Chaque ancien texte doit correspondre exactement une fois ; ajoutez du contexte s'il se répète. `old_text: null` crée un nouveau fichier et ne remplace jamais un fichier existant. Plusieurs modifications du même fichier sont traitées dans l'ordre.

Le patch renvoie un diff unifié. `dry_run` vaut `true` par défaut : aucune écriture. Avec `false`, l'application demande l'autorisation selon la politique choisie (refuser/demander/accepter et autorisations mémorisées), vérifie que les fichiers n'ont pas changé, puis applique le lot. Les écritures des outils source sont sérialisées et une restauration est tentée si une écriture échoue. Le lot n'est pas une transaction résistante à un arrêt brutal du PC. Les fichiers UTF-8, leur BOM et leurs retours à la ligne sont préservés hors des textes remplacés.

Les chemins hors projet, secrets et liens symboliques sont exclus. Grep et patch limitent les fichiers à 128 Ko. Les recherches limitent le nombre d'entrées parcourues, de résultats et la taille de sortie ; les résultats incomplets sont signalés. Réduisez le glob pour poursuivre une recherche volumineuse.

Les chemins reconnus dans les réponses (par exemple `docs/rapport.html`), les chemins entre backticks et les liens Markdown deviennent cliquables, y compris dans l'historique. Pour les chemins contenant des espaces, utilisez `[Rapport](<docs/mon rapport.html>)`. Le clic ouvre l'aperçu local dans le navigateur intégré, avec les autorisations habituelles, dans le projet de la conversation. Les blocs de code et les liens web restent inchangés.

Les outils `write_source` et `edit_source` existants sont conservés pour compatibilité. Le nouveau patch n'accepte pas une chaîne de diff en entrée : il produit le diff à partir des remplacements exacts validés.
