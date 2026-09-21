# Refonte Fluent — branche design

## Périmètre et référence

Refonte de l’application existante : chat, composition, navigation des réglages et cartes de skills. Les contrôles WinUI natifs, les actions et la persistance sont conservés.

Référence utilisateur : `C:/Users/le_ma/AppData/Local/Temp/codex-clipboard-e2ef99a7-e36e-4383-b0ce-1a769f031d0c.png`, 3432 × 1366 pixels, vue générale avec les réglages Skills ouverts. Direction : [Fluent pour Windows](https://learn.microsoft.com/en-us/windows/apps/design/) et [surfaces Mica](https://learn.microsoft.com/en-us/windows/apps/design/style/mica).

Il s’agit d’une amélioration de cette interface, pas d’une reproduction pixel par pixel de la capture. Les fenêtres de test et le contenu diffèrent de la référence ; la comparaison porte sur la hiérarchie, les contrôles, les marges et la lisibilité. Les couleurs Mica varient avec le fond Windows.

## Preuves visuelles

Captures locales dans `artifacts/design/` (non versionnées) :

| Capture | État inspecté | Dimensions capturées |
| --- | --- | --- |
| `winui-chat.jpg` | Chat, barre de titre, métriques sur deux lignes, composition | 1139 × 746 |
| `winui-general.jpg` | Navigation Fluent, trois cartes, actions Enregistrer/Annuler | 787 × 666 |
| `winui-skills.jpg` | Skills personnalisés repliés, toggles navigateur/DOM/RAG | 787 × 666 |
| `electron-chat.png` | Chat avec raisonnement et sous-agent terminé | 1860 × 1122 |
| `electron-skills.png` | Skills avec réglages RAG dépliés | 1859 × 1122 |
| `electron-skills-compact.png` | Réglages dans une fenêtre de 900 × 700 DIP | 1109 × 797 |

WinUI : fenêtre principale demandée à 1440 × 940 pixels, réglages à 1000 × 840 pixels ; captures normalisées par l’outil Windows, pas des pixels CSS. Electron : fenêtre normale de 1500 × 960 DIP et compacte de 900 × 700 DIP ; captures du contenu à l’échelle de l’écran Windows (~1,25), sans le cadre système. La normalisation et les cadres différents interdisent une mesure pixel à pixel avec la référence. Aucune retouche des captures.

Les vues complètes ont été inspectées, puis les régions navigation/cartes, titre et métriques comparées avant/après. Des recadrages supplémentaires n’étaient pas nécessaires : ces régions sont visibles à une taille lisible dans les captures natives.

## Itérations et constats

- **P2 corrigé — barre de titre** : le premier rendu utilisait une couleur transparente pour les boutons système, produisant un rectangle blanc. Une surface sombre opaque a remplacé cette couleur ; le rendu final affiche correctement réduire, agrandir et fermer.
- **P2 corrigé — sélecteur de modèle** : la barre de métriques sur une ligne comprimait le nom du modèle. Sous 920 unités de largeur disponible, le modèle occupe la première ligne et débit/contexte passent sur la suivante. Le nom complet est visible dans `winui-chat.jpg`.
- **P2 corrigé — densité des skills** : les instructions du dossier et les espacements des cartes prenaient trop de place. Instructions regroupées dans un Expander, espaces vides des lignes supprimés. Résultat dans `winui-skills.jpg`.
- **Vérification Electron** : absence de débordement horizontal dans le contenu des réglages à 900 × 700 DIP, y compris avec RAG déplié. Les captures attendent deux frames de rendu pour éviter une image de l’écran précédent.

## Surfaces évaluées

- Typographie : hiérarchie 28/24/14/12 dans les réglages natifs, composition 15, icônes Segoe Fluent ; police système WinUI conservée. Electron utilise Segoe UI Variable/Segoe UI et les polices système de repli. Les textes secondaires restent distincts des titres ; noms longs et descriptions peuvent revenir à la ligne.
- Espacement : navigation latérale intégrée, cartes de rayon 8, marges de 16/24, boutons principaux visibles ; colonne de lecture bornée à 1120 unités. Les contrôles gardent leurs comportements natifs.
- Couleurs : ressources de surfaces/texte/bordures WinUI ; fond Mica visible, états sémantiques des outils conservés. Electron utilise des gris neutres et un accent bleu clair. Les rendus finaux ont été inspectés visuellement ; pas d’audit WCAG exhaustif.
- Images/icônes : logo natif existant conservé ; commandes principales et navigation utilisant la police d’icônes Microsoft. Pas d’illustration générée ni de logo redessiné. Les icônes historiques des outils/contenus Electron restent en place.
- Texte : champs RAG et navigateur natifs localisés selon la langue active ; champs d’API masqués en mode RAG local. Certains anciens formulaires ont encore leurs libellés bilingues, hors de cette passe visuelle.

## Validation et limites

- Compilation WinUI Release : réussie, zéro avertissement et zéro erreur.
- Publication standalone : `artifacts/release/win-x64/OhMyHarness.App.exe`. `artifacts/official` n’a pas été modifié.
- Smoke Electron : réussi, comprenant les conversations concurrentes avec réglages ouverts, le Markdown, le scroll, les sous-agents, les modèles composés, MCP, le navigateur, Git, les terminaux et l’export. L’option `OHMYHARNESS_DESIGN_QA=1` ajoute les captures et le contrôle de débordement.
- WinUI : démarrage réel et inspection du chat, de Général et de Skills. Navigation et fermeture sans enregistrer contrôlées. Les réglages utilisent toujours une fenêtre indépendante des agents.
- macOS, contraste élevé, grossissement du texte et très petites fenêtres natives non vérifiés en session réelle. Le responsive Electron a été vérifié ; le mode compact natif est implémenté mais nécessite encore un essai à cette largeur.

Résultat : refonte visuelle validée sur les états Windows inspectés et les parcours Electron testés. Les limites ci-dessus restent explicites ; aucune certification de conformité Fluent ou d’accessibilité complète n’est revendiquée.
