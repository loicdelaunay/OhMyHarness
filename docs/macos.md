# Windows et macOS avec Uno Platform

La solution possède deux projets applicatifs : `OhMyHarness.Core` et `OhMyHarness.App`. Le second utilise Uno Platform pour partager l’interface, les réglages, les conversations et les outils entre Windows et macOS. L’ancien service JSON se trouve dans `Core/Hosting` ; le dossier Electron `desktop` est conservé pour compatibilité, sans être nécessaire à la nouvelle publication.

Voir [le guide Uno, tâches et modèles](uno-tasks-models.md) pour l’architecture et les nouvelles fonctions.

## Compiler et publier

Sur Mac, installer le SDK .NET 10 et les outils de ligne de commande Xcode :

```bash
dotnet build src/OhMyHarness.App -f net10.0-desktop -c Release
dotnet run --project src/OhMyHarness.App -f net10.0-desktop -c Release
dotnet run --project tests/OhMyHarness.Tests -c Release

bash ./publish-macos.sh arm64 # Apple Silicon
bash ./publish-macos.sh x64   # Intel
```

La publication autonome se trouve dans `artifacts/release/osx-arm64` ou `osx-x64`, avec le lanceur `OhMyHarness.App`. Elle ne nécessite ni installation .NET ni Electron. Le script ne produit pas de DMG signé et ne configure pas de notarisation Apple. Pour une distribution publique, signer et notariser sur Mac ; conserver les dépendances natives livrées avec la publication.

Sur Windows, `publish.ps1` conserve la cible WinUI native. `publish.ps1 -UnoDesktop -OutputDirectory artifacts/release-uno` permet de publier la cible Uno Desktop. Le dossier `artifacts/official` reste destiné aux publications réalisées par l’utilisateur.

## Adaptations

| Fonction | Windows natif | Uno Desktop / macOS |
| --- | --- | --- |
| Interface, modèles, CRON, historique, agents, skills | Interface partagée | Interface partagée |
| Navigateur | WebView2 intégré | Fenêtre Chrome/Edge dédiée par conversation, pilotée par CDP |
| DOM, JavaScript, capture et clavier web | API WebView2 | API CDP |
| Aperçus locaux | Origine virtuelle approuvée | Serveur loopback limité au dossier approuvé |
| Terminal | PowerShell | zsh sur Mac, PowerShell sur Windows |
| Souris et clavier | API Windows | CoreGraphics sur Mac |
| Capture bureau/application | API Windows | `screencapture`, cible par identifiant de fenêtre, curseur superposé |
| Clés API | DPAPI CurrentUser | Trousseau macOS |

Installer Chrome ou Edge pour le navigateur Uno Desktop, ou renseigner son chemin dans les réglages. Le processus possède un profil par conversation dans `Browser/` et son arrêt ne ferme pas les autres panneaux. Chrome MCP reste optionnel et nécessite Node.js. Git, OpenCode, Docker et les serveurs MCP sont nécessaires uniquement aux fonctions qui les utilisent.

Les données (`database.sqlite`, `skills/`, `MCP.json`, profils, Python et temporaires) restent dans le dossier portable de l’exécutable. Placer la publication dans un dossier accessible en écriture. Les clés sont liées au compte système : celles provenant de Windows DPAPI ou de l’ancienne interface Electron doivent être ressaisies dans Uno sur Mac. Le reste de l’historique est conservé par les migrations EF Core. Ne pas ouvrir une ancienne version sur la base migrée sans sauvegarde.

Dans **Réglages Système → Confidentialité et sécurité**, autoriser l’**Accessibilité** pour souris/clavier et l’**Enregistrement de l’écran** pour les captures. Les permissions applicatives restent nécessaires et ne remplacent pas les droits macOS. `keyboard_keys` décrit les touches disponibles pour l’OS.

## Validation

Les builds Windows natif et Uno Desktop, les 413 contrôles Core, les deux EXE autonomes et les neuf contrôles Chromium ont été vérifiés. Le test UI contrôle aussi les cases des modèles et deux exécutions d’une tâche avec outil et historique, via un fournisseur local simulé. Les cibles macOS ARM64 et Intel ont été compilées depuis Windows. Le workflow `.github/workflows/desktop.yml` prévoit les builds et tests Windows, Mac ARM64 et Mac Intel.

**À vérifier sur un Mac réel :** démarrage et fermeture, pickers, trousseau après redémarrage, permissions TCC, touches Command/Option et Unicode, curseur et coordonnées des captures Retina/multi-écrans, lancement de Python embarqué, signature/notarisation. Une compilation croisée ne valide pas ces interactions natives.

Référence : [publication Uno Desktop](https://platform.uno/docs/articles/uno-publishing-desktop.html).
