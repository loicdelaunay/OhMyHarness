# Conventions du projet

## Dossiers de publication

- Après toute modification qui touche le CLI, republier le CLI Windows dans `artifacts/CLI` avec `./publish-cli.ps1 -OutputDirectory artifacts/CLI`.
- Après toute modification qui touche la GUI, republier l’application graphique dans `artifacts/GUI` avec `./publish.ps1 -OutputDirectory artifacts/GUI` (cible Uno Desktop par défaut).
- Si un changement touche à la fois la GUI et le CLI, republier les deux avec leurs commandes respectives.
- `artifacts/TEMP` est réservé aux publications temporaires réalisées par l’IA pour ses tests. Utiliser un sous-dossier dédié à chaque test et supprimer les fichiers de publication créés une fois les tests terminés, même en cas d’échec.
- Ne pas créer d’autres dossiers de publication dans `artifacts`. Avant tout nettoyage, vérifier que le chemin ciblé est bien dans `artifacts/TEMP` et supprimer uniquement les fichiers créés pour le test concerné ; préserver les autres fichiers et les données utilisateur.

## Version et changelog

- Pour chaque nouveauté importante ou modification fonctionnelle majeure, incrémenter la version de l’application et ajouter une entrée datée au `CHANGELOG.md` à la racine. Créer ce fichier s’il n’existe pas encore.
- Suivre SemVer pour `ApplicationDisplayVersion` dans `src/OhMyHarness.App/OhMyHarness.App.csproj` : augmenter le numéro mineur pour une nouvelle fonctionnalité compatible, le numéro de correctif pour une correction importante, et le numéro majeur en cas de changement incompatible. Incrémenter aussi `ApplicationVersion` dans ce projet afin que l’identifiant numérique de version reste à jour.
- Si la modification concerne aussi l’hôte Electron historique, garder sa version alignée dans `desktop/package.json` et `desktop/package-lock.json`.
- Décrire dans le changelog le changement visible pour l’utilisateur et son impact, dans une nouvelle entrée en tête de fichier. Ne pas créer d’entrée pour une modification purement interne, documentaire ou mineure.
