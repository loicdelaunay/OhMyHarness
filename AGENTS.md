# Conventions du projet

## Dossiers de publication

- Après chaque mise à jour du logiciel (GUI, CLI ou moteur partagé), republier systématiquement **les deux interfaces**, même si la modification ne concerne directement qu’une seule interface.
- Publier le CLI Windows dans `artifacts/CLI` avec `./publish-cli.ps1 -OutputDirectory artifacts/CLI`.
- Publier aussi la GUI Windows dans `artifacts/GUI` avec `./publish.ps1 -OutputDirectory artifacts/GUI` (cible Uno Desktop par défaut). Une publication du CLI seul ne termine pas une mise à jour.
- Vérifier la réussite des deux publications et l’alignement de leurs versions avant de terminer. Une modification uniquement documentaire ne nécessite pas de reconstruire les exécutables.
- `artifacts/TEMP` est réservé aux publications temporaires réalisées par l’IA pour ses tests. Utiliser un sous-dossier dédié à chaque test et supprimer les fichiers de publication créés une fois les tests terminés, même en cas d’échec.
- Ne pas créer d’autres dossiers de publication dans `artifacts`. Avant tout nettoyage, vérifier que le chemin ciblé est bien dans `artifacts/TEMP` et supprimer uniquement les fichiers créés pour le test concerné ; préserver les autres fichiers et les données utilisateur.

## Releases GitHub

- Ne modifier **GitHub** (push du code, documentation publiée, workflows/pipeline, tags, releases ou leurs assets) **que sur demande explicite de l’utilisateur**, par exemple « mets à jour le GitHub » ou « crée une release ». Respecter la portée de sa demande : un push demandé seul n’implique pas la création d’une release.
- Une modification locale, une nouvelle version, un changelog ou une publication dans `artifacts/GUI` et `artifacts/CLI` n’autorisent pas à eux seuls une mise à jour de GitHub. Préparer les fichiers locaux nécessaires sans les publier tant que l’utilisateur ne le demande pas.
- Lors de la publication d’une version sur GitHub, distribuer systématiquement **la GUI et le CLI**, à la même version. Un push du code ou une release CLI seule ne suffit pas.
- Utiliser les tags `vX.Y.Z` pour la GUI et `cli-vX.Y.Z` pour le CLI, avec des archives Windows x64 `OhMyHarness-vX.Y.Z-win-x64.zip` et `OhMyHarness-CLI-vX.Y.Z-win-x64.zip`. Conserver ces noms compatibles avec la mise à jour automatique.
- Joindre un fichier `SHA256SUMS.txt` à chaque release et vérifier les assets publiés. Préparer les deux releases en brouillon avant de les rendre publiques ; mettre à jour les deux liens de téléchargement du README.
- Distribuer uniquement l’exécutable, la licence et les instructions de démarrage : ne jamais inclure la base SQLite, les clés, les logs, les profils ou les données utilisateur. Préparer les archives dans un dossier dédié sous `artifacts/TEMP`, puis supprimer uniquement ce dossier après vérification.

## Version et changelog

- Pour chaque nouveauté importante ou modification fonctionnelle majeure, incrémenter la version de l’application et ajouter une entrée datée au `CHANGELOG.md` à la racine. Créer ce fichier s’il n’existe pas encore.
- Suivre SemVer pour `ApplicationDisplayVersion` dans `src/OhMyHarness.App/OhMyHarness.App.csproj` : augmenter le numéro mineur pour une nouvelle fonctionnalité compatible, le numéro de correctif pour une correction importante, et le numéro majeur en cas de changement incompatible. Incrémenter aussi `ApplicationVersion` dans ce projet afin que l’identifiant numérique de version reste à jour.
- Si la modification concerne aussi l’hôte Electron historique, garder sa version alignée dans `desktop/package.json` et `desktop/package-lock.json`.
- Décrire dans le changelog le changement visible pour l’utilisateur et son impact, dans une nouvelle entrée en tête de fichier. Ne pas créer d’entrée pour une modification purement interne, documentaire ou mineure.
