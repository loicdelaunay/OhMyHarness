# OhMyHarness

Application Windows native **WinUI 3 / .NET 10**, pour discuter avec OpenAI, DeepSeek ou un serveur compatible avec l’API OpenAI Chat Completions v1.

## Démarrer

Lancer `artifacts\release\win-x64\OhMyHarness.App.exe`, puis **Réglages → Fournisseurs**. Choisir le fournisseur dans la barre supérieure avant de configurer sa clé, son URL et son modèle. **Charger les modèles / tester la clé** interroge `/models` ; certains serveurs compatibles peuvent ne pas exposer cette route, le modèle reste saisissable manuellement.

**Réglages → Général** permet de choisir Français ou English. La langue est appliquée après Enregistrer et conservée entre les lancements ; les noms des projets et le contenu des conversations ne sont pas traduits. Dans le champ de message, **Entrée envoie**, **Ctrl+Entrée insère un saut de ligne** à la position du curseur (ou remplace la sélection).

**Réglages → Skills** propose les skills exploration et édition des sources, recherche web, terminal, revue de code, planification et synthèse. Exploration et Web sont actifs par défaut. Désactiver un skill retire ses outils ; l’édition des sources inclut leur lecture. La navigation web distante nécessite en plus l’autorisation « Accès IA au navigateur », tandis qu’une ouverture locale demande un accord ponctuel. Les choix sont globaux et sauvegardés en SQLite.

Les préréglages sont OpenAI (`https://api.openai.com/v1`, `gpt-4.1-mini`) et DeepSeek (`https://api.deepseek.com`, `deepseek-flash`). La capacité image et la fenêtre de contexte sont configurables : ajuster la limite à celle publiée pour le modèle choisi. La valeur initiale de 128 000 tokens est une configuration utilisateur, pas une détection automatique.

## Fonctionnalités

- Panneau **Outils** à droite avec onglets Web, Terminal, Git et Fichiers. Le bouton **⛶** agrandit le panneau à toute la zone de travail ; cliquer à nouveau le restaure. Sur une fenêtre étroite, le panneau occupe automatiquement la zone de travail et **×** ramène au chat.
- Zone de saisie avec **+** en bas à gauche (images, dossier source, activation rapide des skills, templates, réglages) et bouton d’envoi intégré à droite. Un template préremplit le brouillon sans l’envoyer ; un brouillon existant n’est remplacé qu’après confirmation.
- **Réglages → Templates** : modifier, créer, supprimer des templates et réinitialiser le template **Web app**, qui demande une application autonome en un fichier HTML sans dépendances. Les modifications sont sauvegardées dans SQLite ; Annuler les abandonne.
- **Terminal** : commandes PowerShell dans le dossier du projet, sortie/code de retour, arrêt et délai de 60 secondes. Chaque commande utilise une nouvelle session non interactive ; `cd` et les variables ne persistent pas entre commandes. Activer le skill Terminal pour permettre à l’IA de proposer une commande, obligatoirement soumise à une confirmation détaillée avant exécution. Les commandes lancées manuellement par Exécuter ont les droits du compte Windows et ne sont pas sandboxées.
- **Git** : statut, changements non indexés et indexés, lecture seule. Git doit être installé et accessible dans le PATH. Les fichiers non suivis sont listés dans le statut, leur contenu n’est pas inclus dans le diff.
- **Fichiers** : parcourir le dossier du projet, revenir au parent, lire les fichiers texte et demander leur ouverture dans Web.
- **Web local** : bouton dossier, chemin absolu dans la barre d’adresse, ou outil IA `open_local_file`. Une fenêtre d’autorisation affiche le fichier et son dossier de ressources ; accepter permet le rendu HTML/JS et les ressources de ce dossier dans une origine locale dédiée. Les chemins réseau, liens symboliques/jonctions, sorties du dossier et fichiers exclus sont bloqués. L’autorisation est révoquée au changement de projet, à la prochaine ouverture locale et au redémarrage. Refuser ne lit ni ne navigue vers le fichier.
- Les demandes de lecture/écriture IA hors du dossier source affichent également une autorisation ponctuelle. Les fichiers explicitement exclus restent bloqués. Les permissions caméra/micro/localisation ne sont jamais accordées.

- Projets avec conversations indépendantes ; création, renommage et suppression.
- Historique persistant, restauration du dernier projet, de la conversation et du fournisseur.
- Streaming SSE, arrêt de génération et conservation des réponses interrompues (exclues des requêtes suivantes).
- Jusqu’à quatre images PNG/JPEG/WebP par message, de 8 Mo maximum chacune ; images conservées en SQLite et envoyées en contenu multimodal.
- Un dossier source partagé par toutes les conversations d’un projet. L’IA peut lister les sous-dossiers et lire les fichiers texte à la demande. Aucun index complet ni envoi systématique du dossier.
- Navigateur WebView2 intégré. Activer **Accès IA au navigateur** pour permettre au modèle d’ouvrir des URL et de lire le texte visible et les liens. Le navigateur permet de suivre un lien via son URL ; les outils ne remplissent pas de formulaires et ne cliquent pas sur des boutons.
- Boucle d’outils jusqu’à douze appels au modèle par envoi, journal visible et historique des résultats persisté.
- Débit tokens/s : estimation `≈` pendant le streaming, remplacée par le nombre de tokens de sortie déclaré par l’API divisé par la durée du flux. La latence avant le premier delta n’est pas incluse. Les tokens de raisonnement sont inclus si le fournisseur les compte dans `completion_tokens`.
- Contexte : tokens d’entrée de la dernière requête, fournis par l’API, rapportés à la limite configurée. Aucune estimation présentée comme exacte ; aucun compactage automatique. Pour une conversation trop longue, créer une nouvelle conversation.
- Interface française sombre, barre latérale repliable, navigateur disposé à droite ou dessous selon la largeur.

## Données et confidentialité

`%LOCALAPPDATA%\OhMyHarness\harness.db` contient la configuration, l’état de navigation de l’application, les conversations, les messages d’outils et les images. **EF Core applique les migrations au démarrage** avec `Database.MigrateAsync()` ; migration initiale et snapshot sont versionnés.

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
