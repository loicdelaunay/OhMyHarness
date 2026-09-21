# Navigateur, RAG et sous-agents

## Navigateur

L'ouverture du panneau Outils et le changement d'onglet ne démarrent plus le navigateur. Dans Web, utiliser la flèche de navigation pour démarrer WebView2. Une erreur de création ou un événement de panne du processus affiche une erreur limitée au navigateur ; les autres outils et conversations restent disponibles. Une navigation en attente est terminée avec une erreur lorsque le processus s'arrête.

Dans Réglages → Navigateur, choisir WebView2 intégré, Chrome MCP ou Désactivé. Chrome MCP expose les outils de Chrome DevTools à l'agent, dans une fenêtre externe ; les outils WebView2 sont retirés de son catalogue. Chrome et Node.js/npm doivent être installés. Un chemin Chrome personnalisé est facultatif. La première connexion propose l'autorisation MCP et peut télécharger le paquet npm. Les autorisations d'outils MCP et le mode Plan restent appliqués. Les profils Chrome sont séparés par conversation dans `Chrome/chat-<id>` à côté de l'exécutable. Le MCP est fermé en fin de génération ; son profil est conservé. Ne pas lancer manuellement deux instances sur le même profil.

Ce traitement utilise les [événements de processus WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/process-related-events) et [Chrome DevTools MCP](https://github.com/ChromeDevTools/chrome-devtools-mcp). Il ne peut pas empêcher un logiciel de sécurité de terminer directement OhMyHarness. Aucun contournement de la politique antivirus n'est installé.

## Sources et Markdown

`read_source` accepte toutes les extensions et les fichiers sans extension. Les textes UTF sont lus normalement, avec les limites d'extrait existantes. Les binaires sont présentés en hexadécimal : une ligne représente 16 octets, `start_line` et `end_line` permettent de poursuivre la lecture. Ce n'est pas un extracteur de texte PDF, Office ou OCR. Hors des sources autorisées, une validation de lecture/transmission reste nécessaire. Les exclusions de l'exploration automatique, des écritures et de la sandbox restent appliquées.

Le bouton **↓ Auto** suit la discussion. Remonter le chat le désactive ; revenir en bas le réactive. Les blocs Markdown délimités par trois accents graves sont colorés en conservant le texte copiable. La coloration distingue mots-clés, chaînes, nombres et commentaires ; les très grands blocs restent en texte simple pour préserver la réactivité.

## RAG

Dans **Réglages → Skills**, activer **Recherche sémantique RAG** : ses réglages apparaissent directement sous le skill. Le panneau est masqué quand le skill est désactivé, en conservant sa configuration.

- **Local** : MiniLM multilingue L12-v2 quantifié, CPU, environ 118 Mo de poids intégrés à l'EXE. Il prend en charge le français et l'anglais, y compris une question française sur des sources anglaises et l'inverse. Les ressources sont extraites dans `models/minilm_multilingual` à côté des données au premier usage. Aucun téléchargement ou appel API à l'exécution. Le tokenizer préserve la casse et les accents français.
- **API OpenAI v1** : choisir un fournisseur déjà enregistré et le nom d'un modèle d'embeddings (différent d'un modèle de chat). La clé chiffrée de ce fournisseur est réutilisée. Les textes indexés et requêtes sont transmis seulement après autorisation ; les réponses sont normalisées pour la recherche par similarité.

L'agent dispose de `rag_index` (reconstruire), `rag_search` (chercher par sens), `rag_sources` (parcourir les chemins indexés) et `rag_read` (lire les lignes actuelles d'un résultat). L'index est stocké dans SQLite par projet et modèle, avec empreintes des fichiers. La reconstruction est atomique ; les fichiers modifiés depuis l'indexation sont écartés des résultats jusqu'à la prochaine reconstruction. Chaque résultat contient un chemin, une plage de lignes, un extrait et un score de similarité.

Limites : 1 à 2 000 fichiers selon le réglage (500 par défaut), 5 000 passages, 512 Ko par fichier, 1 à 20 résultats (5 par défaut). Les binaires, dépendances et fichiers protégés ne sont pas indexés automatiquement. Les passages très longs sont tronqués pour l'embedding ; utiliser `rag_read` pour lire leur contexte. L'indexation peut prendre du temps et consommer des appels API. Les autres conversations continuent pendant l'indexation. La reconstruction est indisponible en mode Plan ; RAG est indisponible en sandbox, où les outils sources lisent la copie isolée.

Après la mise à jour de l'ancien modèle local anglais, demander à l'agent de **réindexer les sources avec `rag_index`**. Les anciens vecteurs sont ignorés pour éviter des comparaisons incompatibles, puis remplacés à la reconstruction. Les index API ne changent pas.

Modèle : [paraphrase-multilingual-MiniLM-L12-v2](https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2), export ONNX quantifié Xenova, licence Apache-2.0 incluse avec les ressources. Pooling moyen, normalisation L2, 384 dimensions, fenêtre de 128 tokens. Moteur : Microsoft.ML.OnnxRuntime 1.30.0 ; tokenizer SentencePiece : Microsoft.ML.Tokenizers 2.0.0.

## Sous-agents

Les sous-agents créés par l'orchestrateur OhMyHarness apparaissent dans des bulles du chat et, pendant leur travail, en enfants de la conversation dans la colonne de gauche. Cliquer ouvre leur tâche, activité et échanges. Le bouton Retour permet de rejoindre le parent. La saisie du parent est désactivée dans cette vue pour éviter un envoi au mauvais destinataire ; la génération du parent continue.

À la fin, l'entrée latérale disparaît et la bulle reste consultable. Les échanges sont conservés dans SQLite et rechargés avec la conversation. Un travail encore marqué actif après un redémarrage est affiché comme interrompu. Les sous-agents internes au serveur OpenCode ne sont pas exposés par cette vue.

## Réglages et modèles composés

Les réglages utilisent une liste de catégories à gauche, avec un contenu défilant à droite. Ils restent dans une fenêtre indépendante des agents sous Windows.

Dans **Fournisseurs → + Modèle composé**, choisir un fournisseur déjà enregistré et un modèle pour l’orchestrateur, puis ajouter de 1 à 6 sous-agents. Chacun a un nom unique, un fournisseur, un modèle et une tâche. **Charger les modèles** permet de consulter les modèles du fournisseur. Le modèle composé peut être modifié, dupliqué, supprimé et choisi dans le sélecteur habituel des fournisseurs.

Au début de chaque tour, les tâches configurées s’exécutent en parallèle avec la demande utilisateur ; l’orchestrateur reçoit leurs résultats et poursuit le travail. Sélectionner une composition implique l’orchestration Forced. Les clés sont celles des fournisseurs référencés ; elles ne sont pas copiées dans la composition. Les générations en cours conservent leur configuration même si les réglages changent.

Les sous-agents conservent les limites de l’orchestrateur OhMyHarness : 8 étapes chacun, 6 sous-agents au total par tour, accès sources et permissions hérités, sans terminal/MCP/navigateur ni récursion. Les sous-agents via OpenCode restent en mode lecture/Plan. OpenCode, y compris comme sous-agent, reste indisponible en sandbox. Choisir des tâches indépendantes pour éviter des écritures sur les mêmes fichiers.

## Messages pendant une génération

La saisie et le bouton d’envoi restent utilisables. Le sélecteur au-dessus de la saisie propose :

- **File d’attente** (par défaut) : démarrer un nouveau tour après le travail courant, avec le fournisseur sélectionné au moment de l’envoi.
- **Dans l’exécution en cours** : intégrer une consigne et ses images à la prochaine étape, avec le modèle de l’exécution actuelle. La requête HTTP déjà en cours ne peut pas être modifiée. L’insertion attend la fin de la réponse et de ses appels d’outils pour conserver un historique valide. Pour le serveur OpenCode, il faut attendre la fin de sa réponse courante.

Les messages en attente apparaissent au-dessus de la saisie, peuvent être retirés et sont persistés par conversation dans SQLite. Après une erreur, un arrêt volontaire ou un redémarrage, **Reprendre la file** permet de relancer les messages restants. Ils ne sont pas automatiquement exécutés au démarrage. Une consigne n’est retirée de la file qu’avec la sauvegarde de son message utilisateur. Les nouveaux messages d’une autre conversation restent indépendants.

## Validation des réglages, compositions et envois

Tests de service avec API locale simulée : ordre consigne/file, arrêt et reprise, isolation par conversation, routage des modèles et clés, résultats transmis à l’orchestrateur et mode Plan des sous-agents. Tests SQLite : réouverture des données, transfert des images et consommation unique. Test Electron réel : navigation latérale, panneau RAG conditionnel, création/modification d’une composition et envoi disponible pendant une génération.

## Validation des outils

Tests .NET : formats arbitraires, lecture binaire, coloration et échappement HTML, migrations, sous-agents persistés, vrai MiniLM multilingue, conformité du tokenizer français à la référence, recherche français/français et français/anglais dans les deux sens, remplacement de l'ancien index et exclusion des fichiers périmés. Tests de service : embeddings via un serveur OpenAI v1 simulé, aucun envoi après refus. Test Electron : démarrage du panneau sans navigateur, Auto, navigation des sous-agents et interface encore réactive après arrêt volontaire du renderer du navigateur. Test Chrome optionnel : `dotnet run --project tests/OhMyHarness.Tests -c Release -- --chrome-smoke` utilise un profil indépendant et un Chrome sans fenêtre visible.

La politique antivirus d'entreprise et l'interface macOS demandent une vérification sur leurs machines respectives.
