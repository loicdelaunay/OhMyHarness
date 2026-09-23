# Uno Platform, tâches planifiées et modèles

La solution applicative contient deux projets :

- `src/OhMyHarness.Core` : données SQLite/EF, migrations, fournisseurs, agents, outils, planification CRON et adaptateurs de bureau. L’ancien service JSON est conservé dans `Hosting` pour les tests et la compatibilité avec l’ancienne interface.
- `src/OhMyHarness.App` : interface commune Uno Platform 6.7, en C#/XAML. Cibles `net10.0-windows10.0.19041.0` (WinUI Windows) et `net10.0-desktop` (Skia Windows/macOS). Les projets de tests restent séparés de la solution applicative.

La base `database.sqlite`, les skills, MCP.json, Python embarqué, les profils du navigateur et les ressources restent dans le dossier portable de l’exécutable. Les migrations ajoutent les tâches et les catalogues sans supprimer les conversations. Ne lancez pas une ancienne version du logiciel sur une base déjà migrée sans sauvegarde.

## Tâches d’un projet

Ouvrir **Tâches planifiées** dans la barre latérale. Créer une tâche, saisir son nom et son instruction, puis choisir sa fréquence : minutes, heures, chaque jour, chaque semaine ou CRON personnalisé à cinq champs. Le fuseau horaire est conservé ; les trois prochaines échéances sont prévisualisées. Les pas CRON se réinitialisent à chaque heure/jour (par exemple `*/40` signifie minutes 0 et 40 de chaque heure).

Les sections **Modèle et réflexion** et **Ressources et outils** permettent de régler le fournisseur, modèle, réflexion, contexte, capacité image, dossiers/fichiers, mode Plan/Exécution, orchestration, sandbox, auto-continue et skills. Les dossiers peuvent suivre les valeurs par défaut du projet ou être définis pour la tâche. Les permissions actuelles de l’application et du projet restent applicables : une tâche peut attendre une autorisation ou une réponse interactive.

- **Continuer l’historique** : réutilise sa dernière conversation, avec compaction habituelle.
- Option décochée : crée une conversation vide par exécution. Les conversations précédentes restent consultables.
- Une conversation déjà occupée n’est pas reprise en parallèle. Une tâche ne chevauche pas sa propre exécution.
- La planification fonctionne **pendant que l’application est ouverte**, y compris lorsque les réglages sont ouverts. Elle ne réveille pas l’ordinateur et n’installe pas de service système.
- Au redémarrage, plusieurs échéances manquées sont regroupées en une seule exécution ; la suivante est calculée à partir de l’heure actuelle. Les exécutions interrompues ne sont pas rejouées immédiatement avant la prochaine échéance, pour éviter de reproduire des actions.
- Activer/désactiver, modifier et supprimer se font dans la liste des tâches. La suppression du planning conserve les conversations et ne coupe pas une exécution déjà lancée ; l’arrêt reste disponible dans sa conversation.

Le planning est stocké en UTC avec le fuseau d’origine. Un verrou local évite que deux instances exécutent les mêmes tâches en parallèle. Aucun secret n’est copié dans la définition d’une tâche : elle référence la connexion existante.

## Catalogue des modèles

Chaque card fournisseur propose **Actualiser les modèles**, une liste et des cases à cocher. Dans l’éditeur, **Tester la connexion** détecte les modèles, les coche tous et enregistre immédiatement ce fournisseur. Une actualisation simple conserve les sélections encore proposées par le serveur ; elle ne coche pas automatiquement les nouveaux modèles et ses changements se valident avec **Enregistrer**. Les connexions ayant le même nom conservent des catalogues indépendants.

Le sélecteur du chat regroupe les modèles cochés avec le nom et l’identifiant de la connexion. Changer de modèle sélectionne aussi son fournisseur. Décocher tous les modèles masque cette connexion dans le sélecteur. L’éditeur du fournisseur conserve un identifiant saisissable pour les API sans route `/models` : ce modèle peut être ajouté au catalogue visible.

## Différences de plateforme

La cible Windows native conserve WebView2 intégré. Uno Desktop affiche également la WebView2 de Uno **dans l’onglet Web** du panneau Outils, avec une vue par conversation. Le navigateur démarre à la demande et son arrêt n’interrompt pas les autres panneaux. Navigation, lecture et modification du DOM, JavaScript, clics, clavier, captures et aperçus locaux restent disponibles ; les interactions synthétiques d’Uno Desktop peuvent être limitées sur les sites qui exigent des événements natifs. La capture Windows Uno recadre la vue Web depuis la fenêtre de l’application. Sous Windows, le runtime Edge WebView2 est requis ; sous macOS, Uno utilise WebKit. L’option externe Chrome · MCP nécessite Chrome/Edge et Node.js.

Sur macOS, les clés nouvelles sont conservées dans le trousseau système ; SQLite ne contient que leur référence. Les clés Windows DPAPI et celles de l’ancienne interface Electron doivent être ressaisies lors d’un changement de plateforme/hôte. Les skills souris/clavier utilisent CoreGraphics ; les captures d’application utilisent leur identifiant de fenêtre et incluent un curseur. macOS doit autoriser l’Accessibilité et l’Enregistrement de l’écran. Une autorisation dans l’application ne remplace pas ces permissions système.

Le terminal utilise PowerShell sur Windows et zsh sur macOS. Git, Docker et les serveurs MCP restent des prérequis pour leurs fonctions respectives. Cette migration cible Windows et macOS ; la présence du moteur Uno X11 ne constitue pas une validation Linux.

## Compiler et vérifier

```powershell
dotnet run --project tests/OhMyHarness.Tests -c Release
dotnet build src/OhMyHarness.App -f net10.0-windows10.0.19041.0 -c Release
dotnet build src/OhMyHarness.App -f net10.0-desktop -c Release
# Publication de travail, hors official :
.\publish.ps1 -OutputDirectory artifacts\release
.\publish.ps1 -NativeWinUI -OutputDirectory artifacts\release-winui
```

Sur Mac : `./publish-macos.sh arm64` ou `./publish-macos.sh x64`. Le script publie Uno Desktop autonome, sans Electron. Une signature/notarisation Apple reste nécessaire pour une distribution signée.

Le paramètre de dépôt `-p:OhMyHarnessDesktopOnly=true` limite les builds avec RID à la cible Uno Desktop sans imposer ce TFM au projet Core. Les scripts de publication l’appliquent. Exemple de compilation croisée :

```powershell
dotnet build src/OhMyHarness.App -f net10.0-desktop -p:OhMyHarnessDesktopOnly=true -r osx-arm64 -c Release
```

Un test UI isolé est disponible avec `OHMYHARNESS_UI_SMOKE=<nouveau dossier temporaire>` : il démarre avec une base de test, contrôle les cases des modèles et le sélecteur, enregistre une tâche, exécute deux tours via un fournisseur local simulé avec outil et historique, capture les écrans et ferme uniquement cette instance. `dotnet run --project tests/OhMyHarness.Tests -c Release -- --browser-smoke` teste deux profils Chromium headless sans utiliser les profils personnels.

Références : [Uno SDK et projet partagé](https://platform.uno/docs/articles/features/using-the-uno-sdk.html), [publication Desktop](https://platform.uno/docs/articles/uno-publishing-desktop.html), [capacités et limites du WebView](https://platform.uno/docs/articles/controls/WebView.html).
