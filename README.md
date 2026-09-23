# OhMyHarness

**Marre des harnais IA complets qui demandent une longue configuration, plusieurs services et des outils à installer avant le premier chat ? Et si un EXE portable faisait l’essentiel du travail ?**

OhMyHarness réunit conversations, agents, sources et outils dans une application de bureau. Sous Windows, l’application est distribuée sous forme d’**EXE autonome** : placez-le dans un dossier accessible en écriture, lancez-le et configurez votre fournisseur. L’application crée sa base `database.sqlite` et ses ressources à côté de l’exécutable. Pour déplacer votre espace de travail, copiez ce dossier complet après avoir fermé l’application.

L’interface est construite avec **Uno Platform / .NET 10**. Elle cible Windows (Uno Desktop ou WinUI natif) et macOS (Uno Desktop). Le [parcours macOS](docs/macos.md) reste à valider sur un Mac réel.

## Ce que vous pouvez faire

- **Brancher vos modèles** : plusieurs connexions OpenAI compatibles v1, DeepSeek ou OpenCode, chacune avec sa clé, son URL et ses modèles sélectionnés. Vous pouvez aussi composer un modèle orchestrateur avec des sous-agents spécialisés.
- **Travailler sur plusieurs conversations à la fois** : projets, dossiers sources partagés, générations simultanées, messages en attente ou injectés dans l’exécution, reprise et fork depuis un message.
- **Donner des outils à l’agent** : navigateur intégré avec accès contrôlé au DOM et au JavaScript, terminaux parallèles, exploration et édition de fichiers, recherche de code, aperçu des changements Git, captures d’écran, souris, clavier et Python embarqué.
- **Garder le contrôle** : skills activables, serveurs MCP, modes Plan/Exécution, demandes d’autorisation, sandbox facultative, questions interactives et suivi des tâches de l’agent. Les conventions `AGENTS.md` et les skills `SKILL.md` peuvent être chargés depuis le projet.
- **Retrouver le contexte utile** : mémoire par conversation ou partagée, recherche RAG avec modèle local multilingue ou fournisseur d’embeddings, images et modèle vision de secours, compteur de tokens, débit et compactage du contexte.
- **Adapter votre espace** : tâches planifiées par projet, thèmes clairs et sombres, français/anglais, nom et logo personnalisés, export Markdown et base SQLite avec migrations EF Core.

Les **fournisseurs cloud** demandent une connexion réseau et, selon le service, une clé API ; le modèle de chat ne tourne pas dans l’EXE. Les intégrations facultatives gardent leurs prérequis : Git pour la vue Git, Docker/Podman pour la sandbox, OpenCode pour sa connexion dédiée, ou Chrome/Node.js pour Chrome MCP. Le navigateur intégré s’appuie sur le moteur Web du système (Edge WebView2 sous Windows, WebKit sur macOS). Le cœur de l’application ne nécessite pas de serveur OhMyHarness séparé.

Le [guide des agents](docs/agent-modes.md), le [navigateur et le RAG](docs/browser-rag-agents.md), la [mémoire](docs/memory.md), les [skills personnalisés](docs/skill-authoring.md) et la [sandbox](docs/sandbox.md) détaillent ces fonctions et leurs limites.

## Démarrer

Lancer `artifacts\official\OhMyHarness.App.exe`, puis **Réglages → Fournisseurs**. Chaque connexion possède sa card, sa clé, son URL et son catalogue. **Tester la connexion** détecte les modèles, les coche tous et enregistre ce fournisseur automatiquement. **Actualiser les modèles** conserve les choix existants ; les modifications manuelles se valident avec **Enregistrer**. Le sélecteur du chat regroupe les modèles cochés de toutes les connexions et change automatiquement de fournisseur. Plusieurs connexions du même type restent indépendantes. Pour les API sans route `/models`, le modèle reste saisissable manuellement dans l’éditeur et peut être coché dans la card.

**Tâches planifiées**, dans un projet, permet de créer un planning CRON avec picker, instruction, modèle, réflexion, ressources, skills et choix entre conversation neuve ou historique continu. Les tâches s’exécutent pendant que l’application est ouverte ; elles ne réveillent pas le PC.

**Réglages → Général** permet de choisir Français ou English. La langue est appliquée après Enregistrer et conservée entre les lancements ; les noms des projets et le contenu des conversations ne sont pas traduits. Dans le champ de message, **Entrée envoie**, **Ctrl+Entrée insère un saut de ligne** à la position du curseur (ou remplace la sélection).

**Général → Nom et logo de l’application** permet de personnaliser le nom affiché et de charger un logo avec aperçu. Les réglages sont mémorisés en SQLite ; les logos externes sont copiés dans `branding/`, et les chemins restent relatifs au dossier portable. Copier le dossier complet conserve la personnalisation. Les huit thèmes comprennent **Fly dark** et **Fly light**, inspirés du bleu Airbus officiel. Voir [la personnalisation portable](docs/branding.md).

**Ctrl + molette** ajuste les polices de l’interface par paliers de 10 %, de 80 à 150 %. **Ctrl + 0** rétablit 100 %. La taille est conservée en SQLite et s’applique aussi aux nouveaux messages, au code, au raisonnement et aux fenêtres de réglages, sans désactiver l’auto-scroll. Le contenu du navigateur conserve son propre zoom.

Sous Windows WinUI, les réglages s’ouvrent dans une fenêtre indépendante : les agents continuent leurs générations et leurs outils en arrière-plan. Leurs demandes d’autorisation restent accessibles dans la fenêtre principale. Enregistrer applique les modifications ; Annuler ou fermer la fenêtre abandonne les brouillons des réglages.

**Réglages → Skills** propose les skills exploration et édition des sources, recherche web, terminal, contrôle de la souris, contrôle du clavier, captures d’écran, revue de code, planification et synthèse. Exploration et Web sont actifs par défaut. Désactiver un skill retire ses outils ; l’édition des sources inclut leur lecture. La navigation distante nécessite « Accès IA au navigateur » et l’inspection/interaction nécessite aussi « Accès DOM et interaction IA ». Les choix sont globaux et sauvegardés en SQLite.

Les préréglages sont OpenAI (`https://api.openai.com/v1`, `gpt-4.1-mini`) et DeepSeek (`https://api.deepseek.com`, `deepseek-flash`). La capacité image et la fenêtre de contexte sont configurables : ajuster la limite à celle publiée pour le modèle choisi. La valeur initiale de 128 000 tokens est une configuration utilisateur, pas une détection automatique.

Le bouton **+ OpenCode** crée une connexion dédiée au serveur local OpenCode. L’application peut se connecter à un `opencode serve` déjà lancé ou démarrer automatiquement l’exécutable configuré, importe les modèles accessibles et conserve une session OpenCode distincte par conversation. Le mot de passe du serveur utilise le même stockage DPAPI que les clés API. Les outils agent OpenCode sont désactivables par connexion ; lorsqu’ils sont actifs, leurs demandes passent par le popup Autoriser une fois / Toujours autoriser / Refuser et les autorisations permanentes restent révocables dans Réglages.

## Fonctionnalités

- **+ → Mode** : Plan ou Exécution, par conversation. Plan bloque techniquement les écritures, le terminal, les interactions et MCP. **+ → Orchestration sous-agents** : Disable / Auto / Forced, avec analyses déléguées et résultats conservés dans le chat.
- **AGENTS.md** : chargement automatique des conventions des racines et sous-dossiers associés. **Skills personnalisés** : dossier `skills/` à côté de l'exécutable, modèle `exemple-revue` fourni et chargement à la demande des `SKILL.md` activés. Voir [modes, sous-agents et skills personnalisés](docs/agent-modes.md) pour les limites et l'intégration OpenCode.

- Panneau **Outils** à droite avec onglets Web, Terminal, Git et Fichiers. Le bouton **⛶** agrandit le panneau à toute la zone de travail ; cliquer à nouveau le restaure. Sur une fenêtre étroite, le panneau occupe automatiquement la zone de travail et **×** ramène au chat.
- Zone de saisie avec **+** en bas à gauche (images, dossier source, activation rapide des skills, templates, réglages) et bouton d’envoi intégré à droite. Un template préremplit le brouillon sans l’envoyer ; un brouillon existant n’est remplacé qu’après confirmation.
- **Réglages → Templates** : modifier, créer, supprimer des templates et réinitialiser le template **Web app**, qui demande une application autonome en un fichier HTML sans dépendances. Les modifications sont sauvegardées dans SQLite ; Annuler les abandonne.
- **Terminal** : commandes PowerShell dans le dossier du projet, sortie/code de retour, arrêt et délai de 30 secondes par défaut, configurable par l’IA avec `timeout_seconds` entre 1 et 600 secondes. `start_terminal` rend immédiatement la main pour laisser un serveur tourner en arrière-plan ; `read_terminal`, `wait_terminal` et `stop_terminal` permettent de le suivre ou l’arrêter. Les serveurs doivent rester au premier plan de leur commande (pas de détachement shell), et sont arrêtés au délai demandé. En sandbox, ils sont également arrêtés en fin de génération. Chaque commande utilise une nouvelle session non interactive ; `cd` et les variables ne persistent pas entre commandes. Activer le skill Terminal pour permettre à l’IA de proposer une commande, obligatoirement soumise à une confirmation détaillée avant exécution. Les commandes lancées manuellement par Exécuter ont les droits du compte Windows et ne sont pas sandboxées.
- **Git** : disponible seulement lorsque le dossier racine du projet contient `.git` (dossier ou fichier de worktree). Il liste les fichiers modifiés puis montre les lignes changées, indexées et non indexées, en lecture seule. Les fichiers non suivis sont listés sans afficher leur contenu. Git doit être installé et accessible dans le PATH.
- **Fichiers** : parcourir le dossier du projet, revenir au parent, lire les fichiers texte et demander leur ouverture dans Web.
- **Web local** : bouton dossier, chemin absolu dans la barre d’adresse, ou outil IA `open_local_file`. Une fenêtre d’autorisation affiche le fichier et son dossier de ressources ; accepter permet le rendu HTML/JS et les ressources de ce dossier dans une origine locale dédiée. Les chemins réseau, liens symboliques/jonctions, sorties du dossier et fichiers exclus sont bloqués. Refuser ne lit ni ne navigue vers le fichier.
- Chaque accès sensible propose **Autoriser une fois**, **Toujours autoriser** ou **Refuser**. Une autorisation permanente est limitée au fichier, dossier ou site affiché et sauvegardée en SQLite. **Réglages → Autorisations** liste ces portées et permet de les révoquer. Les fichiers explicitement exclus restent bloqués. Les permissions caméra/micro/localisation ne sont jamais accordées.
- **Réglages → Autorisations → Comportement des demandes d’autorisation** applique une règle globale avant les popups : **Refuser tout**, **Demander** (mode par défaut) ou **Acceptation automatique**. Refuser tout et Acceptation automatique ont priorité sur les autorisations permanentes enregistrées. Les protections de chemins et les exclusions de secrets restent actives dans les trois modes.

- Projets avec conversations indépendantes ; création, renommage et suppression.
- Plusieurs conversations peuvent générer une réponse simultanément (OpenAI compatible, DeepSeek et OpenCode), y compris dans des projets différents. Une barre animée apparaît sous chaque conversation en cours dans la liste. La navigation, les réglages et les brouillons restent disponibles ; **Arrêter** ne coupe que la conversation affichée. Chaque envoi conserve son fournisseur, son modèle, ses sources et ses images. Les brouillons et pièces jointes restent associés au chat pendant la session de l’application. Les outils du navigateur et du bureau partagés sont exécutés successivement pour éviter les collisions ; leurs demandes d’autorisation attendent la fermeture du dialogue précédent.
- Le skill **Contrôle du clavier** expose aussi `keyboard_keys`, qui liste les touches, alias et exemples utilisables. `Alt`, `Ctrl`, `Shift` et `Win` fonctionnent seuls, ainsi que les raccourcis tels que `Ctrl+S` et `Alt+Tab`. `Entrée` et `Enter` sont acceptés. Un appui relâche la touche immédiatement ; il ne maintient pas un modificateur entre deux appels.
- Historique persistant, restauration du dernier projet, de la conversation et du fournisseur.
- Streaming SSE, arrêt de génération et conservation des réponses interrompues (exclues des requêtes suivantes).
- Le bloc **Raisonnement du modèle** s’ouvre pendant le flux et fait défiler son propre contenu vers le bas à chaque mise à jour, indépendamment du défilement principal de la conversation.
- Jusqu’à quatre images PNG/JPEG/WebP par message, de 8 Mo maximum chacune ; images conservées en SQLite et envoyées en contenu multimodal.
- Plusieurs dossiers sources peuvent être associés au même projet et sont partagés par toutes ses conversations. Chaque dossier apparaît comme une puce détachable et comme une racine distincte dans Fichiers ; l’IA peut lister les sous-dossiers et lire les fichiers texte à la demande. Aucun index complet ni envoi systématique des dossiers.
- Navigateur WebView2 intégré. Activer **Accès IA au navigateur** pour ouvrir des URL et lire la page. Activer aussi **Accès DOM et interaction IA** pour exposer au modèle un DOM assaini avec les éléments interactifs, puis autoriser les clics, la saisie, les sélections et le défilement. Le skill **Contrôle de la souris** accepte les clics gauche/droit, le double-clic, le déplacement et la molette dans le navigateur comme sur le bureau Windows multi-écran. Après un popup d’autorisation, la fenêtre précédemment active est restaurée avant une capture ou une action sur le bureau. Les captures du bureau dessinent le curseur enregistré avant le popup ; les captures Web affichent la dernière position manuelle ou pilotée dans WebView2 et sont normalisées aux pixels CSS employés par les clics. Le skill **Contrôle du clavier** saisit du texte dans le contrôle actif et exécute une touche ou un raccourci (`Ctrl+S`, `Alt+Tab`, `Entrée`, touches de fonction) dans le navigateur ou Windows ; la cible doit être mise au focus avant l’action. **Captures d’écran** ajoute l’analyse visuelle ; chaque interaction clavier, souris ou transmission d’image demande une autorisation selon sa portée.
- Boucle d’outils limitée par défaut à douze appels au modèle. **Réglages → Général → Continuer automatiquement après 12 étapes** permet de poursuivre sans envoyer « continue », jusqu’à la réponse finale ou **Arrêter**. Les autorisations restent actives et la compaction peut résumer les anciens groupes d’outils pendant une longue exécution.
- **Réglages → MCP**, après Skills : créer, modifier, supprimer et tester plusieurs serveurs MCP ; transports stdio, HTTP Streamable et SSE. Le menu **+ → MCP** permet de les activer/désactiver rapidement. Voir [la configuration MCP](docs/mcp.md).
- Débit tokens/s : estimation `≈` pendant le streaming, remplacée par le nombre de tokens de sortie déclaré par l’API divisé par la durée du flux. La latence avant le premier delta n’est pas incluse. Les tokens de raisonnement sont inclus si le fournisseur les compte dans `completion_tokens`.
- Contexte : nombre de tokens affiché en temps réel pendant la réponse. Le préfixe `≈` indique l’estimation locale avant que le fournisseur renvoie ses compteurs exacts ; l’affichage additionne l’entrée et la sortie courante. À 95 % de la limite configurée, l’application résume automatiquement les anciens tours complets, conserve les échanges récents et persiste ce résumé pour les appels suivants. Cette compaction déclenche un appel supplémentaire au fournisseur sélectionné.
- Interface française sombre, barre latérale repliable, navigateur disposé à droite ou dessous selon la largeur.

## Données et confidentialité

Le navigateur intégré est propre à chaque conversation : page, historique de navigation pendant la session, cookies, stockage web et accès aux aperçus locaux sont séparés. Changer de conversation affiche son navigateur sans rediriger les appels des agents en arrière-plan. Les profils sont stockés dans `WebView2/chat-<id>/` en WinUI et dans les partitions Chromium `omh-browser-chat-<id>` en Electron. L’ancien profil commun reste conservé mais n’est pas partagé avec les nouveaux profils : les connexions aux sites doivent être refaites par conversation. Les terminaux sont déjà isolés par conversation ; Git et Fichiers consultent les sources du projet associé, avec protection contre les résultats d’une sélection précédente. Deux chats attachés au même dossier réel partagent toujours les fichiers de ce dossier. Souris, clavier et captures du bureau réel restent des ressources du PC, avec les autorisations existantes.

Le bouton **Exporter**, à côté d’**Outils**, copie la conversation en Markdown jusqu’à 512 Kio UTF-8. Au-delà, ou si le presse-papiers est indisponible, il propose un fichier `.md`. L’export contient tous les messages conservés (y compris les tours antérieurs au compactage), les raisonnements enregistrés, les appels/résultats d’outils, les compteurs et les images intégrées en base64. Leur affichage dépend du lecteur Markdown. Une génération active continue et son texte courant est inclus comme instantané partiel. Les réglages exportés sont ceux du moment de l’export, pas un historique par message. Les clés API et secrets de configuration sont exclus ; le contenu des échanges et des résultats d’outils est conservé tel quel.

`database.sqlite`, placé dans le même dossier que `OhMyHarness.App.exe`, contient la configuration, l’état de navigation de l’application, les conversations, les messages d’outils, les images et les autorisations permanentes. **EF Core applique les migrations au démarrage** avec `Database.MigrateAsync()` ; migrations et snapshot sont versionnés. Au premier démarrage de cette version, si `database.sqlite` n’existe pas encore, l’ancienne base `%LOCALAPPDATA%\OhMyHarness\harness.db` est copiée de manière cohérente avec l’API de sauvegarde SQLite afin de conserver les données. L’ancien fichier est laissé intact comme sauvegarde.

Le dossier contenant l’exécutable doit donc être accessible en écriture. Pour un usage portable, placer l’EXE dans un dossier utilisateur plutôt que dans `Program Files`.

Les données gérées par OhMyHarness sont regroupées dans ce dossier : `database.sqlite` (et ses journaux SQLite), `skills/`, `MCP.json`, `WebView2/` pour Windows natif, `Browser/` pour les anciens profils Chromium externes, `sandboxes/`, `temp/`, Python et les fichiers de travail `OpenCodeWorkspaces/`/`opencode-runner.mjs`. Aucun repli vers AppData n’est effectué si le dossier n’est pas accessible en écriture. Les anciens dossiers WinUI `WebView2` et `OpenCodeWorkspaces` d’AppData sont copiés au premier démarrage si leur destination portable n’existe pas, sans supprimer les originaux.

Pour sauvegarder ou déplacer l’application, fermer toutes ses instances puis copier **le dossier complet**. Les dossiers sources associés restent des références à des projets externes. Les logiciels externes (serveur OpenCode, MCP, Docker/Podman et commandes exécutées) conservent leurs propres installations et stockages ; ce mode portable n’est pas une sandbox système. Le runtime .NET de l’EXE unique peut extraire ses composants dans le cache temporaire système. Les clés restent liées au compte système comme indiqué ci-dessous.

Les clés API sont protégées par **Windows DPAPI / CurrentUser** ou le **trousseau macOS**. Copier la base vers un autre compte ou OS impose de ressaisir les clés ; les anciennes clés Electron doivent aussi être ressaisies dans Uno. Le reste de la base n’est pas chiffré. Les données effectivement utilisées (messages, images, fichiers lus et pages lues) sont transmises au fournisseur sélectionné lors de l’envoi.

Le skill d’édition des sources permet de créer et modifier les fichiers du dossier associé ; sans ce skill, les outils sources restent en lecture seule. Les chemins hors du dossier nécessitent une autorisation ponctuelle. Les liens symboliques/jonctions, `.env*`, `secrets.json`, `.git`, `bin`, `obj`, `node_modules` et certains dossiers de build sont exclus des outils sources et de l’aperçu local. La lecture texte est limitée à 128 Ko, une ressource d’aperçu Web à 32 Mo. Ces restrictions ne constituent pas un sandbox pour les commandes terminal autorisées.

Chaque conversation possède sa propre vue du navigateur dans le panneau Outils. Windows natif utilise WebView2 avec un profil par conversation ; Uno Desktop utilise la WebView2 de Uno (Edge WebView2 sous Windows, WebKit sous macOS). Le profil Uno Desktop peut être partagé entre les vues. Chrome · MCP reste un mode externe facultatif. Le blocage des permissions caméra/micro/localisation et des téléchargements est appliqué sur la cible Windows native ; Uno Desktop ne fournit pas ces mêmes événements WebView2.

## Compiler et publier

Prérequis de développement : Windows 10 1809+ / Windows 11, SDK .NET 10, Git LFS pour récupérer le modèle RAG local, SDK Windows et outils de développement WinUI installés via Visual Studio. Ouvrir `OhMyHarness.slnx` dans Visual Studio/Rider ou utiliser :

```powershell
dotnet build src/OhMyHarness.App -c Release
dotnet run --project src/OhMyHarness.App -f net10.0-desktop -c Release
dotnet run --project tests/OhMyHarness.Tests -c Release
.\publish.ps1
```

`run.bat` et `publish.bat` offrent les mêmes actions par double-clic. La publication x64 utilise Uno Desktop et contient **un seul EXE** avec .NET et les composants de l’application embarqués. Les composants sont extraits automatiquement au premier lancement. Le navigateur Web intégré utilise le runtime Edge WebView2 sous Windows ; Chrome ou Edge n’est requis que pour le mode externe Chrome · MCP. La cible WinUI native reste disponible avec `publish.ps1 -NativeWinUI`.

La destination par défaut est `artifacts\official`. Utiliser `publish.ps1 -OutputDirectory <dossier>` pour la changer. Le stockage utilise le chemin réel du processus, car `IncludeAllContentForSelfExtract` redirige `AppContext.BaseDirectory` vers le cache d’extraction. Le test `tests/verify-portable.ps1` vérifie une vraie publication et le déplacement de son EXE.

`publish.ps1 -Runtime win-arm64` permet de cibler ARM64 (non validé sur matériel ARM64). Ne pas supprimer `IncludeAllContentForSelfExtract` du profil de publication.

Pour ajouter une migration :

```powershell
dotnet tool install --global dotnet-ef --version 10.0.9
dotnet ef migrations add NomMigration --project src/OhMyHarness.Core --output-dir Migrations
```

## Structure

| Projet | Rôle |
| --- | --- |
| `src/OhMyHarness.Core` | EF Core, agents, fournisseurs, outils Windows/macOS, CRON, service JSON de compatibilité dans `Hosting` |
| `src/OhMyHarness.App` | Interface Uno Platform Windows/macOS, navigateur, réglages et tâches |
| `desktop` | Ancienne interface Electron et tests d’intégration, conservés pour compatibilité |
| `OhMyHarness.Desktop.slnx` | Solution portable sans dépendance WinUI, à utiliser sur macOS |
| `tests/OhMyHarness.Tests` | Exécutable de tests hors ligne, sans clé API |

## Validation et limites

Les tests couvrent le streaming Unicode/CRLF, les appels d’outils fragmentés, l’usage fournisseur, les flux tronqués, l’annulation, les URL, la protection des sources, les migrations répétées, la persistance des images, les suppressions en cascade et le chiffrement des clés. Les erreurs API sont affichées sans journaliser de clé.

Les appels réels OpenAI/DeepSeek nécessitent une clé utilisateur ; les validations automatisées utilisent un fournisseur simulé. Les réponses disposent d’un rendu Markdown. La recherche sémantique RAG doit être activée et ses sources indexées ; les terminaux lancent des commandes non interactives. Les sites fortement dynamiques peuvent nécessiter une seconde lecture après chargement. Les tests couvrent aussi les templates personnalisés, les commandes PowerShell/annulation, les diffs Git et les protections des chemins de l’aperçu local.

Références : [Chat Completions OpenAI](https://developers.openai.com/api/reference/resources/chat), [API DeepSeek](https://api-docs.deepseek.com/), [vision DeepSeek](https://api-docs.deepseek.com/guides/vision/), [publication WinUI 3 en fichier unique](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app).

- Liste de tâches structurée, questions interactives par conversation et protection après trois appels identiques : [guide du déroulement des agents](docs/workflow.md).

Le chat suit les nouvelles réponses uniquement lorsque le défilement est en bas ; consulter l’historique suspend ce suivi. Le skill web expose `browser_javascript` (avec accès navigateur et DOM activés) pour lire les scripts et variables ou modifier le JavaScript de la page après autorisation. Le code est synchrone, limité à 32 000 caractères et 5 secondes, exécuté dans la page de la conversation, sans accès Node. Les changements sont temporaires jusqu’au rechargement ; utiliser les outils sources pour les enregistrer. Cet outil est interdit en mode Plan et en sandbox.

- Navigateur démarré à la demande, Chrome MCP, lecture de fichiers étendue, suivi Auto, coloration du code, RAG configurable et vues de sous-agents : [guide et limites](docs/browser-rag-agents.md).
- Ressources par conversation, dossiers par défaut, `permission.json`, reprise/fork, TODO, deux vues Git et compatibilité HTTP/DeepSeek : [guide des conversations](docs/conversation-workspace.md).
