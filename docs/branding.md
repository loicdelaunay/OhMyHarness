# Nom, logo et thèmes Fly

Dans **Réglages → Général**, ouvrir **Nom et logo de l’application** :

- Saisir le nom affiché, jusqu’à 80 caractères. Un champ vide rétablit OhMyHarness.
- Choisir un logo PNG, JPEG, WebP, BMP ou ICO (10 Mo, 4096 × 4096 pixels maximum). Un aperçu permet de vérifier l’image avant d’enregistrer.
- **Logo d’origine** restaure seulement le logo. **Rétablir le nom et le logo** réinitialise les deux champs.
- Cliquer sur **Enregistrer** pour appliquer. Annuler conserve la personnalisation précédente.

Le nom apparaît dans la barre latérale et les titres des fenêtres principale, Réglages et Tâches planifiées. Le logo apparaît dans la barre latérale et sert aussi d’icône de fenêtre sous Windows. Le nom du fichier EXE et l’identité système de l’application restent stables.

## Stockage portable

Les champs `ApplicationName` et `LogoPath` sont stockés dans les réglages JSON de `database.sqlite`, sans nouvelle migration. Un logo déjà dans le dossier de l’exécutable ou un sous-dossier est référencé par son chemin relatif, par exemple `mon-logo.png` ou `images/logo.png`.

Un logo extérieur est copié à l’enregistrement dans `branding/`, sous un nom dérivé de son contenu. Son fichier d’origine est conservé. Sous Windows, `branding/window-icon.ico` est une version adaptée pour l’icône de fenêtre. Copier **le dossier complet**, base et images comprises, conserve ces références après déplacement. Les chemins relatifs sont résolus depuis le dossier portable réel, même avec un EXE auto-extractible.

Si un logo manque ou devient illisible, l’interface utilise le logo d’origine ; les réglages permettent de choisir une nouvelle image. Une réinitialisation ne supprime pas les fichiers d’image.

## Fly dark et Fly light

Les deux thèmes sont disponibles dans **Général → Thème**, en français comme en anglais. **Fly dark** associe un fond presque noir à des surfaces bleu profond ; **Fly light** associe un fond blanc à des surfaces neutres. Le bleu Airbus **#00205B** marque les actions et les sélections dans les deux thèmes, sans accent bleu clair. Les textes sur les boutons bleus restent blancs pour conserver leur lisibilité.

La couleur de référence est publiée dans le [Brand Centre officiel d’Airbus](https://www.brand.airbus.com/en/asset-library/airbus-logo). Il s’agit de thèmes inspirés de cette palette ; aucun logo Airbus n’est fourni avec l’application.
