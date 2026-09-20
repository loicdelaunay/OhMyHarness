# Windows et macOS

L’interface WinUI 3 originale dépend de Windows. Elle est conservée, avec son EXE autonome et WebView2. Le dossier `desktop` fournit une seconde interface Electron pour macOS (Apple Silicon et Intel) et Windows. Les migrations EF Core, SQLite, les clients fournisseurs et la protection des sources restent partagés dans `OhMyHarness.Core`.

Le processus Electron lance `OhMyHarness.Service` et communique par son entrée/sortie standard : aucun port réseau privilégié n’est ouvert. Les pages du navigateur intégré sont isolées de l’interface et n’ont accès ni à Node.js, ni aux clés, ni au pont IPC de l’application.

## Fonctions et adaptations

| Fonction | Windows WinUI | Interface desktop sur macOS |
| --- | --- | --- |
| Fournisseurs multiples, OpenAI compatible, DeepSeek | Conservés | Implémentés avec les clients Core partagés |
| OpenCode | Connexion au serveur et lancement configuré | Connexion à `opencode serve` ou lancement du CLI installé ; chemin configurable |
| Projets, sources multiples, images, historique | Conservés | Implémentés, même schéma SQLite et migrations |
| Conversations simultanées, arrêt indépendant, brouillons | Conservés | Implémentés, progression par conversation |
| Débit et contexte, compaction à 95 % | Conservés | Implémentés ; estimation locale en attendant les compteurs API |
| Réglages FR/EN, skills, templates | Conservés | Implémentés |
| Web, inspection DOM et interactions | WebView2 | Chromium isolé ; mêmes catégories d’outils |
| Fichiers HTML locaux et ressources | Approbation avant ouverture | Approbation puis origine locale limitée au dossier validé |
| Terminal | PowerShell | `/bin/zsh -c`, nouvelle session par commande |
| Git | Lecture des modifications et diffs | Identique ; nécessite Git dans le PATH |
| Souris, clic gauche/droit, double-clic, défilement | API Windows | CoreGraphics, permission Accessibilité macOS |
| Clavier et liste des touches | Ctrl, Alt, Shift, Win | Command/Cmd, Option/Alt, Control/Ctrl, Shift ; touches filtrées par plateforme |
| Capture avec curseur | Capture Windows | `screencapture -C`, permission d’enregistrement de l’écran |
| Permissions demander/refuser/automatique et autorisations permanentes | Conservées | Même politique applicative, avec permissions système macOS supplémentaires |
| Chiffrement des clés | DPAPI CurrentUser | Electron safeStorage, adossé au trousseau macOS |

Les outils souris/clavier/capture sont disponibles uniquement lorsque leur skill est activé. `keyboard_keys` expose la liste adaptée à l’OS ; le prompt indique aussi le système et son shell. Un appui relâche les touches immédiatement. Les outils de bureau et du navigateur partagés sont sérialisés entre conversations. Les chemins macOS sont comparés sans supprimer la distinction majuscules/minuscules ; les protections contre les sorties de dossier, liens et fichiers sensibles s’appliquent aussi sur Mac.

OpenCode utilise ses propres outils et permissions lorsqu’ils sont activés dans la connexion. Installer son CLI séparément ou indiquer l’URL d’un serveur existant ; le lancement du binaire interne d’OpenCode Desktop n’est pas pris en charge par ce nouvel hôte.

## Compiler sur Mac

Prérequis de développement : SDK .NET 10, Node.js 24 avec npm, et outils de ligne de commande Xcode. Git est nécessaire au panneau Git. Le packaging doit être exécuté sur macOS.

```bash
# Depuis la racine du dépôt, sur Apple Silicon :
bash ./publish-macos.sh arm64

# Variante Intel :
bash ./publish-macos.sh x64
```

Le script compile la solution portable, exécute ses tests, publie un service .NET autonome puis produit les paquets `.dmg` et `.zip` dans `desktop/dist`. L’utilisateur final n’a besoin ni de Node.js, ni du runtime .NET, ni de WebView2 : Chromium et le service sont embarqués dans le bundle `.app`. Ce n’est pas un EXE Windows sur Mac.

Pour développer sans packaging :

```bash
dotnet build OhMyHarness.Desktop.slnx -c Release
cd desktop
npm ci
node scripts/publish-service.cjs osx-arm64 # osx-x64 sur Intel
npm test
npm start
```

Pour tester ce nouvel hôte sur Windows, utiliser `win-x64` à la place de `osx-arm64`. La version WinUI reste publiée par `publish.ps1` ; sa solution est `OhMyHarness.slnx`. Sur Mac, utiliser uniquement `OhMyHarness.Desktop.slnx`, qui n’inclut pas WinUI.

## Données et permissions macOS

En version packagée, `database.sqlite` est créé **à côté du bundle `OhMyHarness.app`**, hors de son contenu signé. Placer l’application dans un dossier utilisateur accessible en écriture et sortir l’application du DMG avant de l’utiliser. En développement, la base se trouve dans `desktop/.data/database.sqlite`. Le cache et les cookies Chromium utilisent le dossier portable `browser/`, distinct de cette base.

Le profil Chromium (`browser/`), les skills (`skills/`), les copies sandbox (`sandboxes/`), les temporaires (`temp/`), les journaux et rapports de crash sont également placés à côté du bundle `.app`. Le profil navigateur de l’ancienne version Electron n’est pas importé automatiquement. Configuration, historique, images et autorisations sont stockés dans SQLite. Les clés sont chiffrées par le trousseau macOS, sans repli en clair. Une base copiée depuis Windows conserve ses données, mais ses clés DPAPI doivent être ressaisies sur Mac ; l’inverse s’applique également. Ne pas ouvrir la même base simultanément depuis les deux interfaces. La signature et l’identité de l’application doivent rester stables pour conserver l’accès au trousseau.

Dans les réglages de l’application, le bouton de permissions macOS aide à vérifier **Accessibilité** et **Enregistrement de l’écran**. Accorder les droits nécessaires dans **Réglages Système → Confidentialité et sécurité**, puis relancer l’application si macOS le demande. En développement, macOS peut afficher Electron ou le service comme processus demandeur. La politique « Acceptation automatique » de l’application n’accorde pas ces droits système à sa place. Les permissions caméra, microphone, localisation et les téléchargements du navigateur restent refusés.

Les commandes terminal autorisées s’exécutent avec les droits du compte utilisateur. Elles ne constituent pas un environnement isolé. L’installation de Git/OpenCode et les changements du système restent à la charge de l’utilisateur.

## Signature et distribution

La configuration electron-builder inclut le service dans la signature et fournit les entitlements nécessaires à Electron/.NET (JIT et chargement de bibliothèques natives). Pour une distribution publique, configurer une identité Developer ID et les identifiants de notarisation Apple via les mécanismes sécurisés d’electron-builder ; ne pas les placer dans le dépôt. Aucune identité Apple ni notarisation n’a été configurée ici.

Le workflow `.github/workflows/desktop.yml` prépare des builds Windows, Mac ARM64 et Mac Intel. Il n’a pas été exécuté pendant cette modification. Il nécessite GitHub Actions ; sur une forge Gitea, prévoir un runner et un workflow adaptés, avec un hôte macOS pour produire l’application Mac.

## Validation

Vérifié depuis Windows : compilation de WinUI et du service portable, publication autonome du service pour `win-x64`, `osx-arm64` et `osx-x64`, tests Core et intégration avec fournisseur HTTP simulé. Un test Electron vérifie le rendu, le streaming, le changement de conversation pendant une réponse et l’absence du pont privilégié dans une page distante.

**À valider sur un Mac réel avant diffusion :** lancement du bundle signé, accès au trousseau après redémarrage/mise à jour, permissions TCC, clics gauche/droit, raccourcis Command/Option sur clavier français, saisie Unicode, curseur dans les captures Retina/multi-écrans, et signature/notarisation des paquets. La compilation croisée du service ne valide pas ces interactions natives. Les appels réels aux fournisseurs nécessitent les clés de l’utilisateur.

Références : [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/), [chiffrement Electron safeStorage](https://www.electronjs.org/docs/latest/api/safe-storage), [signature .NET sur macOS](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos), [packaging macOS electron-builder](https://www.electron.build/v26/docs/mac/).
