# MCP et continuation automatique

Les interfaces WinUI Windows et Electron Windows/macOS proposent **Réglages → MCP**, immédiatement après Skills, et **+ → MCP** pour activer ou désactiver chaque serveur.

## Ajouter un serveur

Choisir **Ajouter un serveur MCP**, donner un nom et sélectionner le transport :

- `stdio` : exécutable (`node`, `npx`, `uvx`, chemin absolu…), arguments sous forme de tableau JSON, dossier de travail facultatif. L’exécutable doit être installé. Sur Windows, pour un lanceur `.cmd` qui exige un shell, utiliser explicitement `cmd.exe` avec ses arguments ; sur Mac, utiliser le lanceur Unix ou son chemin absolu.
- `http` : URL MCP Streamable HTTP, par exemple `https://serveur.example/mcp`.
- `sse` : endpoint SSE des anciens serveurs. Utiliser HTTP Streamable pour les serveurs récents.

HTTPS est requis à distance ; HTTP est accepté sur localhost. Les secrets éventuels sont saisis dans **Secrets JSON**, par exemple :

```json
{
  "environment": { "MY_API_KEY": "valeur" },
  "headers": { "Authorization": "Bearer valeur" }
}
```

`environment` s’applique à stdio, `headers` à HTTP/SSE. Laisser ce champ vide lors d’une modification conserve les secrets enregistrés ; la case dédiée les efface. Ils sont chiffrés avec DPAPI sous Windows ou le trousseau macOS. Ne pas placer les secrets dans les arguments ou l’URL, qui restent des champs de configuration ordinaires. Les serveurs locaux reçoivent les variables système usuelles et celles configurées, pas tous les secrets de l’environnement de l’application.

Sous WinUI, **Tester la connexion MCP** utilise le brouillon ; **Enregistrer** applique tous les changements. Sous Electron, enregistrer le serveur puis cliquer sur **Tester la connexion**. Le résultat liste les outils annoncés par le serveur. Une configuration n’installe pas automatiquement les dépendances du serveur ; un lanceur tel que `npx -y …` peut le faire lorsqu’il est exécuté après approbation.

## Utilisation par l’agent

Les outils des serveurs activés sont ajoutés aux appels des fournisseurs **OpenAI compatibles et DeepSeek**. Chaque serveur possède un espace de noms séparé pour éviter les collisions entre outils homonymes. Les résultats texte/structurés sont transmis au modèle ; une image PNG/JPEG/WebP de 8 Mo maximum peut également être jointe si le modèle accepte les images. Les ressources et prompts MCP ne disposent pas encore d’un explorateur dédié, et l’authentification OAuth interactive n’est pas implémentée : utiliser les en-têtes ou variables fournis par le serveur.

**OpenCode conserve ses propres serveurs MCP et sa propre boucle d’outils.** Les serveurs configurés ici ne sont pas copiés dans sa configuration. L’option de continuation concerne la limite de douze appels imposée par OhMyHarness aux fournisseurs compatibles Chat Completions.

Connexion et exécution des outils respectent **Demander / Refuser tout / Acceptation automatique**. « Toujours autoriser » reste limité au serveur et, pour une action, à l’outil concerné ; changer la commande, l’URL, les arguments ou les secrets invalide la portée précédente. Les approbations restent révocables dans les paramètres.

Les réglages et interrupteurs sont globaux. Une désactivation bloque les appels suivants, y compris un appel encore en attente d’autorisation ; elle n’annule pas une opération déjà exécutée. **Arrêter** annule la conversation et ferme ses connexions. Chaque conversation possède ses connexions stdio : plusieurs conversations peuvent donc lancer plusieurs instances du même serveur. Les connexions refusées ou défaillantes ne sont pas relancées en boucle ; corriger la configuration ou démarrer un nouvel envoi pour retenter.

## Auto-continuation

**Réglages → Général → Continuer automatiquement après 12 étapes** est désactivé par défaut. Activé, l’agent conserve son historique et poursuit ses appels après la douzième étape. Il s’arrête quand il produit une réponse finale, rencontre une erreur bloquante ou reçoit **Arrêter**. L’option n’accepte aucune autorisation à la place de l’utilisateur. La désactiver ramène l’arrêt à la prochaine frontière de douze étapes.

La configuration est stockée dans `database.sqlite`, avec une migration EF Core qui conserve les données existantes. L’auto-continuation peut consommer davantage de tokens. Les opérations MCP sont limitées à 30 secondes pour la connexion et deux minutes pour un appel d’outil.

## Validation

Tests automatisés locaux : migrations/persistance, serveurs MCP simulés stdio et HTTP, authentification par en-tête, transmission des variables, refus avant lancement, désactivation avant exécution, annulation pendant un outil, limite de douze appels et quatorze appels avec continuation active. Tests d’interface Electron : emplacement de l’onglet, création/modification et interrupteur du menu +. Les serveurs tiers et l’exécution native sur Mac nécessitent leurs propres essais.

L’intégration utilise le [SDK officiel MCP C#](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/transports/transports.html), version 2.2.0.
