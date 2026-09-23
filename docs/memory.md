# Mémoire persistante

**Réglages → Mémoire** active deux niveaux de skill indépendants, également accessibles dans **Skills** et le menu **+** :

- **Conversation** : informations privées au fil courant, conservées après redémarrage et compactage du contexte.
- **Partagée** : informations réutilisables entre conversations, selon leur catégorie.

Les catégories sont **Projet**, **Général** et **Utilisateur**. Une mémoire partagée Projet est limitée au projet courant. Les mémoires partagées Général et Utilisateur sont accessibles entre projets. Une mémoire de conversation reste privée à ce fil quelle que soit sa catégorie. Désactiver un skill conserve les données mais retire cet accès au modèle.

**Voir la mémoire** ouvre une liste avec filtres de projet, conversation, portée et catégorie. La recherche interroge les clés, titres, contenus et tags. Les pages contiennent 20 résultats ; **Afficher plus** charge la suite. Un clic affiche le contenu complet, la version, la dernière modification et le fil d’origine. Les créations, éditions et suppressions manuelles sont enregistrées immédiatement ; le bouton général **Annuler** ne les annule pas. Les interrupteurs de skill, eux, sont validés avec **Enregistrer**.

## Outils du modèle

| Outil | Fonction |
| --- | --- |
| `memory_search` | Recherche par mots/préfixes sans accents ; requête vide pour les entrées récentes. Filtres, pagination, extraits et versions. |
| `memory_read` | Contenu complet et provenance d’un ID accessible. |
| `memory_save` | Création par clé stable ou modification avec `id` et `expected_version`. |
| `memory_delete` | Suppression avec `id` et `expected_version`. |

Exemple de création :

```json
{"scope":"shared","category":"project","key":"validation.build","title":"Validation du projet","content":"Compiler avec dotnet build avant de proposer une modification.","tags":"build validation"}
```

Les écritures du modèle passent par le système d’autorisations de l’application (demander, refuser, acceptation automatique et autorisations permanentes). En **mode Plan**, seuls les outils de lecture sont autorisés. Les sous-agents utilisent la mémoire de leur conversation parente, avec les mêmes limites ; deux écritures sur la même version ne peuvent pas s’écraser silencieusement. La portée, catégorie et clé restent immuables lors d’une modification.

Les outils sont intégrés au runtime natif OpenAI v1/DeepSeek et au host portable. Le serveur OpenCode conserve son propre catalogue : les outils de mémoire de l’application ne lui sont pas exposés actuellement. En sandbox, l’accès passe par le service mémoire contrôlé par l’application ; le fichier SQLite n’est pas monté dans le conteneur.

## Stockage et recherche

Tout est enregistré dans **database.sqlite**, à côté de l’exécutable : table EF Core `Memories` et index SQLite FTS5 `MemorySearch`. La migration conserve les données existantes. Des triggers synchronisent automatiquement l’index lors des créations, éditions, suppressions et cascades.

La recherche combine des mots littéraux avec AND, accepte les préfixes et neutralise les opérateurs FTS saisis par le modèle. Elle classe les correspondances avec BM25, en privilégiant le titre, la clé et les tags. Les filtres de portée sont appliqués avant la pagination et aussi lors de chaque lecture ou écriture par ID. Il s’agit d’une recherche lexicale locale, sans embeddings ni service externe.

La recherche renvoie des extraits courts ; le contenu complet est limité à 16 000 caractères par entrée. Aucune injection systématique de toute la mémoire dans le contexte : le modèle cherche puis lit les entrées pertinentes. Les instructions du skill lui demandent de conserver des faits utiles, d’éviter les secrets et les doublons, et de traiter les souvenirs comme des données à vérifier.

Supprimer une conversation efface sa mémoire privée. Supprimer un projet efface ses mémoires de projet. Les mémoires partagées Général/Utilisateur restent disponibles, avec une origine vide si le fil d’origine a été supprimé. Un fork ne copie pas automatiquement la mémoire privée du fil d’origine.
