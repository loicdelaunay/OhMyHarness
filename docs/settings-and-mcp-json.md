# Réglages, thèmes et MCP.json

Général propose six thèmes persistés dans le champ de réglages SQLite existant : Fluent sombre, Minuit, Forêt, Fluent clair, Ivoire et Brume. Les raccourcis de saisie restent actifs, mais leur ancien label a été retiré des paramètres.

Les fournisseurs sont affichés en cartes. La roue dentée ouvre le formulaire de la connexion concernée ; ajout, duplication, suppression et modèles composés restent disponibles.

Les informations du modèle, le débit, le contexte et la saisie partagent un même bloc. Son bouton d’en-tête replie ou déplie les informations et conserve ce choix dans SQLite.

## Configuration MCP portable

`MCP.json` est créé dans le dossier portable de l’application, à côté de `database.sqlite` et de l’EXE Windows, via `PortableStorage.Root` (jamais dans le cache d’extraction du binaire standalone). Le host macOS fournit son dossier portable au service.

Dans les réglages MCP, **Éditer MCP.json** ouvre l’éditeur. Exemple accepté :

```json
{
  "mcpServers": {
    "godot": {
      "command": "npx",
      "args": ["@coding-solo/godot-mcp"],
      "env": {
        "GODOT_PATH": "/path/to/godot",
        "DEBUG": "true"
      }
    }
  }
}
```

Remplacer le chemin par celui de Godot. Aucune connexion n’est lancée lors de l’enregistrement ; les connexions et appels utilisent les autorisations MCP existantes. Les serveurs sont activés par défaut dans ce format ; `"enabled": false` permet de les désactiver. Pour HTTP/SSE : `url`, `transport` et `headers` sont également acceptés. Le dossier de travail facultatif est `cwd`.

Le fichier est validé avant application à SQLite, qui conserve les identifiants et les secrets protégés nécessaires au moteur. Les modifications manuelles sont chargées au démarrage/à la consultation de la configuration et lors du chargement des outils. Un JSON invalide laisse les serveurs précédents disponibles. Une modification externe intervenue pendant l’édition demande un rechargement au lieu d’être écrasée.

Les secrets déjà enregistrés dans SQLite ne sont jamais exportés en clair. Les valeurs `env`/`headers` écrites explicitement dans le JSON restent naturellement présentes dans le fichier et sont chiffrées dans SQLite. Si un secret est ensuite remplacé via le formulaire, l’ancienne valeur explicite est retirée du fichier. La comparaison interne des secrets inchangés préserve les empreintes des autorisations. `MCP.json` est exclu de Git.

Les noms des serveurs doivent être uniques. Les ajouts, suppressions et toggles du formulaire sont répercutés dans le fichier. Si formulaire et fichier sont modifiés simultanément, recharger le fichier avant d’enregistrer.

## Validation

- Contrôles .NET de persistance, parsing du format Godot, absence d’export des secrets, synchronisation et conflits de fichier.
- Tests du service pour les API JSON, les six thèmes et la synchronisation des toggles.
- Smoke Electron : thème clair, cartes fournisseurs, éditeur JSON et repli du panneau de saisie.
- Compilation et publication WinUI Windows ; validation native visuelle interrompue par l’utilisateur (Échap). Aucun contrôle visuel macOS réalisé.
