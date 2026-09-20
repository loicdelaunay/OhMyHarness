# Sandbox de développement (Windows et macOS)

Dans **+ → Sandbox**, activez le mode pour les prochains envois de cette conversation. Le bouton **ⓘ** explique le fonctionnement en français ou en anglais. La préférence est stockée dans SQLite, avec migration automatique. Un envoi déjà en cours conserve son mode initial.

## Prérequis

Installez et démarrez **Docker Desktop** en mode conteneurs Linux, ou **Podman** avec sa machine Linux. Téléchargez l'image de développement avant utilisation :

```sh
docker pull node:22-bookworm
# ou
podman pull node:22-bookworm
```

L'application privilégie Docker puis Podman. Elle ne télécharge pas d'image automatiquement et ne retombe **jamais** sur le terminal local si le moteur est indisponible. Les API OpenAI compatibles et DeepSeek restent appelées par l'application, hors conteneur.

## Fonctionnement

- Une copie distincte et persistante des sources par conversation, sous `sandboxes/` près de la base SQLite. La copie initiale comprend les modifications non commitées. Elle ne suit pas automatiquement les changements ultérieurs de l'original.
- Les outils de lecture, recherche, écriture et patch, ainsi que les sous-agents, utilisent cette copie. Les chemins extérieurs restent interdits même si les autorisations globales sont sur « accepter tout ».
- Chaque commande de terminal démarre un conteneur éphémère. Les sources arrivent par archive ; **aucun montage des dossiers hôtes, du socket Docker, des clés API ou de SQLite**. Seuls les fichiers sources réguliers validés reviennent dans la copie. Les liens, traversées de chemin, périphériques et archives surdimensionnées sont rejetés.
- Réseau techniquement désactivé (`--network=none`). Il n'existe pas encore d'option pour ouvrir certains domaines. Les dépendances nécessaires doivent déjà se trouver dans l'image ; installations Internet et serveurs persistants ne sont pas disponibles.
- Système racine en lecture seule, toutes les capacités Linux retirées, `no-new-privileges`, commandes avec UID 1000. Le superviseur utilise un autre UID non privilégié, pour empêcher une commande de suspendre son arrêt automatique.
- 1 CPU, 512 Mio de RAM, 128 processus ; volumes temporaires de 128 Mio et 64 Mio. 60 secondes par commande, 30 minutes par génération. Le bouton Arrêter déclenche la destruction du conteneur, y compris les processus d'arrière-plan. Une durée de vie de 120 secondes sert de protection supplémentaire si l'application se ferme brutalement.
- Sources limitées à 64 Mio, 10 000 fichiers, 2 Mio par fichier. Le filtre exclut `.git`, dépendances, sorties de compilation, secrets connus et formats non autorisés. **Un secret incorporé dans un fichier source n'est pas détecté automatiquement.**

## Revoir et appliquer

Après la génération : **+ → Examiner les modifications sandbox…**. La revue affiche les créations, modifications et suppressions par rapport à la copie initiale. Pour les binaires, elle affiche le type de changement et la taille. Cliquez explicitement sur **Appliquer ces modifications** pour écrire dans le projet original. Cette étape ne passe pas par les autorisations automatiques. Les fichiers d'origine sont revérifiés octet par octet ; en cas de changement externe, l'application refuse le lot.

Les originaux restent intacts si vous fermez la revue. La copie reste disponible pour poursuivre la conversation. Désactiver Sandbox revient au mode local au prochain envoi sans effacer la copie. Si les dossiers sources du projet changent, créez une nouvelle conversation pour démarrer une autre copie.

## Périmètre de cette première version

**OpenCode, MCP, navigateur et contrôle du bureau sont bloqués en sandbox** : leur exécution isolée n'est pas encore intégrée. Les activer dans les Skills ne contourne pas cette restriction. Les outils manuels du panneau droit restent locaux ; ils ne sont pas les outils de l'agent sandbox.

`git_changes` affiche le diff de la copie privée. L'historique Git et les configurations du dépôt ne sont pas copiés ; un terminal peut initialiser un dépôt jetable dans son conteneur. Seules les sources sont conservées entre commandes, pas `.git`, les dépendances ou les serveurs en arrière-plan. Les conteneurs Linux ne permettent pas de compiler WinUI ni d'utiliser les SDK macOS natifs.

Les limites utilisent les mécanismes documentés de [Docker](https://docs.docker.com/engine/containers/run/). Cette protection reste celle du moteur de conteneurs et de sa VM ; ce n'est pas une VM de bureau avec accès souris/clavier.

Validation locale : tests de copie, archives hostiles, conflits, revue/application, migration et interface. Un moteur Docker/Podman opérationnel est nécessaire pour valider le cycle réel sur chaque OS.
