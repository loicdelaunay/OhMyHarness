# OhMyHarness

Application Windows native **WinUI 3 / .NET 10**, pour discuter avec OpenAI, DeepSeek, OpenCode ou un serveur compatible avec l’API OpenAI Chat Completions v1.

## Démarrer

Lancer `artifacts\release\win-x64\OhMyHarness.App.exe`, puis **Réglages → Fournisseurs**. Cet écran gère de zéro à autant de connexions que nécessaire, y compris plusieurs instances OpenAI compatibles ou DeepSeek. Les boutons de création et de duplication sont indépendants du type : chaque instance conserve sa propre clé chiffrée, son nom, son URL, son modèle, sa capacité image et sa limite de contexte. Elles peuvent avoir le même nom ou le même endpoint et être modifiées ou supprimées séparément. Le fournisseur sélectionné dans Réglages devient actif et reste ensuite interchangeable depuis la barre supérieure. **Charger les modèles / tester la clé** interroge `/models` pour l’instance éditée ; certains serveurs compatibles peuvent ne pas exposer cette route, le modèle reste saisissable manuellement.

**Réglages → Général** permet de choisir Français ou English. La langue est appliquée après Enregistrer et conservée entre les lancements ; les noms des projets et le contenu des conversations ne sont pas traduits. Dans le champ de message, **Entrée envoie**, **Ctrl+Entrée insère un saut de ligne** à la position du curseur (ou remplace la sélection).

**Réglages → Skills** propose les skills exploration et édition des sources, recherche web, terminal, contrôle de la souris, contrôle du clavier, captures d’écran, revue de code, planification et synthèse. Exploration et Web sont actifs par défaut. Désactiver un skill retire ses outils ; l’édition des sources inclut leur lecture. La navigation distante nécessite « Accès IA au navigateur » et l’inspection/interaction nécessite aussi « Accès DOM et interaction IA ». Les choix sont globaux et sauvegardés en SQLite.

Les préréglages sont OpenAI (`https://api.openai.com/v1`, `gpt-4.1-mini`) et DeepSeek (`https://api.deepseek.com`, `deepseek-flash`). La capacité image et la fenêtre de contexte sont configurables : ajuster la limite à celle publiée pour le modèle choisi. La valeur initiale de 128 000 tokens est une configuration utilisateur, pas une détection automatique.

Le bouton **+ OpenCode** crée une connexion dédiée au serveur local OpenCode. L’application peut se connecter à un `opencode serve` déjà lancé ou démarrer automatiquement l’exécutable configuré, importe les modèles accessibles et conserve une session OpenCode distincte par conversation. Le mot de passe du serveur utilise le même stockage DPAPI que les clés API. Les outils agent OpenCode sont désactivables par connexion ; lorsqu’ils sont actifs, leurs demandes passent par le popup Autoriser une fois / Toujours autoriser / Refuser et les autorisations permanentes restent révocables dans Réglages.

## Fonctionnalités

- Panneau **Outils** à droite avec onglets Web, Terminal, Git et Fichiers. Le bouton **⛶** agrandit le panneau à toute la zone de travail ; cliquer à nouveau le restaure. Sur une fenêtre étroite, le panneau occupe automatiquement la zone de travail et **×** ramène au chat.
- Zone de saisie avec **+** en bas à gauche (images, dossier source, activation rapide des skills, templates, réglages) et bouton d’envoi intégré à droite. Un template préremplit le brouillon sans l’envoyer ; un brouillon existant n’est remplacé qu’après confirmation.
- **Réglages → Templates** : modifier, créer, supprimer des templates et réinitialiser le template **Web app**, qui demande une application autonome en un fichier HTML sans dépendances. Les modifications sont sauvegardées dans SQLite ; Annuler les abandonne.
- **Terminal** : commandes PowerShell dans le dossier du projet, sortie/code de retour, arrêt et délai de 60 secondes. Chaque commande utilise une nouvelle session non interactive ; `cd` et les variables ne persistent pas entre commandes. Activer le skill Terminal pour permettre à l’IA de proposer une commande, obligatoirement soumise à une confirmation détaillée avant exécution. Les commandes lancées manuellement par Exécuter ont les droits du compte Windows et ne sont pas sandboxées.
- **Git** : disponible seulement lorsque le dossier racine du projet contient `.git` (dossier ou fichier de worktree). Il liste les fichiers modifiés puis montre les lignes changées, indexées et non indexées, en lecture seule. Les fichiers non suivis sont listés sans afficher leur contenu. Git doit être installé et accessible dans le PATH.
- **Fichiers** : parcourir le dossier du projet, revenir au parent, lire les fichiers texte et demander leur ouverture dans Web.
- **Web local** : bouton dossier, chemin absolu dans la barre d’adresse, ou outil IA `open_local_file`. Une fenêtre d’autorisation affiche le fichier et son dossier de ressources ; accepter permet le rendu HTML/JS et les ressources de ce dossier dans une origine locale dédiée. Les chemins réseau, liens symboliques/jonctions, sorties du dossier et fichiers exclus sont bloqués. Refuser ne lit ni ne navigue vers le fichier.
- Chaque accès sensible propose **Autoriser une fois**, **Toujours autoriser** ou **Refuser**. Une autorisation permanente est limitée au fichier, dossier ou site affiché et sauvegardée en SQLite. **Réglages → Autorisations** liste ces portées et permet de les révoquer. Les fichiers explicitement exclus restent bloqués. Les permissions caméra/micro/localisation ne sont jamais accordées.
- **Réglages → Autorisations → Comportement des demandes d’autorisation** applique une règle globale avant les popups : **Refuser tout**, **Demander** (mode par défaut) ou **Acceptation automatique**. Refuser tout et Acceptation automatique ont priorité sur les autorisations permanentes enregistrées. Les protections de chemins et les exclusions de secrets restent actives dans les trois modes.

- Projets avec conversations indépendantes ; création, renommage et suppression.
- Historique persistant, restauration du dernier projet, de la conversation et du fournisseur.
- Streaming SSE, arrêt de génération et conservation des réponses interrompues (exclues des requêtes suivantes).
- Le bloc **Raisonnement du modèle** s’ouvre pendant le flux et fait défiler son propre contenu vers le bas à chaque mise à jour, indépendamment du défilement principal de la conversation.
- Jusqu’à quatre images PNG/JPEG/WebP par message, de 8 Mo maximum chacune ; images conservées en SQLite et envoyées en contenu multimodal.
- Plusieurs dossiers sources peuvent être associés au même projet et sont partagés par toutes ses conversations. Chaque dossier apparaît comme une puce détachable et comme une racine distincte dans Fichiers ; l’IA peut lister les sous-dossiers et lire les fichiers texte à la demande. Aucun index complet ni envoi systématique des dossiers.
- Navigateur WebView2 intégré. Activer **Accès IA au navigateur** pour ouvrir des URL et lire la page. Activer aussi **Accès DOM et interaction IA** pour exposer au modèle un DOM assaini avec les éléments interactifs, puis autoriser les clics, la saisie, les sélections et le défilement. Le skill **Contrôle de la souris** accepte les clics gauche/droit, le double-clic, le déplacement et la molette dans le navigateur comme sur le bureau Windows multi-écran. Après un popup d’autorisation, la fenêtre précédemment active est restaurée avant une capture ou une action sur le bureau. Les captures du bureau dessinent le curseur enregistré avant le popup ; les captures Web affichent la dernière position manuelle ou pilotée dans WebView2 et sont normalisées aux pixels CSS employés par les clics. Le skill **Contrôle du clavier** saisit du texte dans le contrôle actif et exécute une touche ou un raccourci (`Ctrl+S`, `Alt+Tab`, `Entrée`, touches de fonction) dans le navigateur ou Windows ; la cible doit être mise au focus avant l’action. **Captures d’écran** ajoute l’analyse visuelle ; chaque interaction clavier, souris ou transmission d’image demande une autorisation selon sa portée.
- Boucle d’outils jusqu’à douze appels au modèle par envoi, journal visible et historique des résultats persisté.
- Débit tokens/s : estimation `≈` pendant le streaming, remplacée par le nombre de tokens de sortie déclaré par l’API divisé par la durée du flux. La latence avant le premier delta n’est pas incluse. Les tokens de raisonnement sont inclus si le fournisseur les compte dans `completion_tokens`.
- Contexte : nombre de tokens affiché en temps réel pendant la réponse. Le préfixe `≈` indique l’estimation locale avant que le fournisseur renvoie ses compteurs exacts ; l’affichage additionne l’entrée et la sortie courante. À 95 % de la limite configurée, l’application résume automatiquement les anciens tours complets, conserve les échanges récents et persiste ce résumé pour les appels suivants. Cette compaction déclenche un appel supplémentaire au fournisseur sélectionné.
- Interface française sombre, barre latérale repliable, navigateur disposé à droite ou dessous selon la largeur.

## Données et confidentialité

`database.sqlite`, placé dans le même dossier que `OhMyHarness.App.exe`, contient la configuration, l’état de navigation de l’application, les conversations, les messages d’outils, les images et les autorisations permanentes. **EF Core applique les migrations au démarrage** avec `Database.MigrateAsync()` ; migrations et snapshot sont versionnés. Au premier démarrage de cette version, si `database.sqlite` n’existe pas encore, l’ancienne base `%LOCALAPPDATA%\OhMyHarness\harness.db` est copiée de manière cohérente avec l’API de sauvegarde SQLite afin de conserver les données. L’ancien fichier est laissé intact comme sauvegarde.

Le dossier contenant l’exécutable doit donc être accessible en écriture. Pour un usage portable, placer l’EXE dans un dossier utilisateur plutôt que dans `Program Files`.

Les clés API sont chiffrées avec **Windows DPAPI / CurrentUser**. Copier la base vers un autre compte Windows ne permet pas de récupérer les clés : il faut les ressaisir. Le reste de la base n’est pas chiffré. Les données effectivement utilisées (messages, images, fichiers lus et pages lues) sont transmises au fournisseur sélectionné lors de l’envoi.

Le skill d’édition des sources permet de créer et modifier les fichiers du dossier associé ; sans ce skill, les outils sources restent en lecture seule. Les chemins hors du dossier nécessitent une autorisation ponctuelle. Les liens symboliques/jonctions, `.env*`, `secrets.json`, `.git`, `bin`, `obj`, `node_modules` et certains dossiers de build sont exclus des outils sources et de l’aperçu local. La lecture texte est limitée à 128 Ko, une ressource d’aperçu Web à 32 Mo. Ces restrictions ne constituent pas un sandbox pour les commandes terminal autorisées.

Le profil Chromium (cache/cookies) est conservé par WebView2 dans `%LOCALAPPDATA%\OhMyHarness\WebView2`, hors de la base applicative. Permissions caméra/micro/localisation et téléchargements sont désactivés. L’accès IA au navigateur doit être réactivé à chaque lancement.

## Compiler et publier

Prérequis de développement : Windows 10 1809+ / Windows 11, SDK .NET 10, SDK Windows et outils de développement WinUI installés via Visual Studio. Ouvrir `OhMyHarness.slnx` dans Visual Studio/Rider ou utiliser :

```powershell
dotnet build src/OhMyHarness.App -c Release
dotnet run --project src/OhMyHarness.App -c Release
dotnet run --project tests/OhMyHarness.Tests -c Release
.\publish.ps1
```

`run.bat` et `publish.bat` offrent les mêmes actions par double-clic. La publication x64 contient **un seul EXE**, avec .NET et Windows App SDK embarqués. Les composants sont extraits automatiquement au premier lancement. Le navigateur exige le **runtime Microsoft Edge WebView2**, généralement déjà installé sous Windows 11 ; il n’est pas embarqué dans l’EXE. L’application reste utilisable pour le chat si le navigateur ne peut pas s’initialiser.

`publish.ps1 -Runtime win-arm64` permet de cibler ARM64 (non validé sur matériel ARM64). Ne pas supprimer `IncludeAllContentForSelfExtract` ni `EnableMsixTooling` du profil de publication.

Pour ajouter une migration :

```powershell
dotnet tool install --global dotnet-ef --version 10.0.9
dotnet ef migrations add NomMigration --project src/OhMyHarness.Core --output-dir Migrations
```

## Structure

| Projet | Rôle |
| --- | --- |
| `src/OhMyHarness.Core` | EF Core, entités, migrations, DPAPI, client HTTP/SSE, accès aux sources |
| `src/OhMyHarness.App` | Application WinUI 3, interface, WebView2, orchestration des outils |
| `tests/OhMyHarness.Tests` | Exécutable de tests hors ligne, sans clé API |

## Validation et limites

Les tests couvrent le streaming Unicode/CRLF, les appels d’outils fragmentés, l’usage fournisseur, les flux tronqués, l’annulation, les URL, la protection des sources, les migrations répétées, la persistance des images, les suppressions en cascade et le chiffrement des clés. Les erreurs API sont affichées sans journaliser de clé.

Les appels réels OpenAI/DeepSeek nécessitent une clé utilisateur ; les validations automatisées utilisent un fournisseur simulé. Les réponses disposent d’un rendu Markdown. Il n’y a pas d’index de recherche sémantique ni de terminal interactif persistant. Les sites fortement dynamiques peuvent nécessiter une seconde lecture après chargement. Les tests couvrent aussi les templates personnalisés, les commandes PowerShell/annulation, les diffs Git et les protections des chemins de l’aperçu local.

Références : [Chat Completions OpenAI](https://developers.openai.com/api/reference/resources/chat), [API DeepSeek](https://api-docs.deepseek.com/), [vision DeepSeek](https://api-docs.deepseek.com/guides/vision/), [publication WinUI 3 en fichier unique](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app).
