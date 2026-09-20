# Modes, sous-agents et skills personnalisés

Dans **+**, chaque conversation possède deux réglages mémorisés en SQLite et capturés au prochain envoi. Modifier un réglage ne transforme pas une génération déjà en cours ; arrêtez-la avant de relancer avec un autre mode.

## Plan et Exécution

**Exécution** conserve les outils activés et leurs autorisations habituelles.

**Plan** ne se contente pas d'une instruction au modèle : les outils interdits sont retirés des définitions et leurs appels sont rejetés avant exécution, même si le fournisseur émet un appel inattendu ou si les autorisations sont en acceptation automatique. Les lectures de sources, glob/grep, Git, l'inspection d'une page déjà ouverte et les captures restent disponibles selon les skills activés. Écriture, patch, terminal (y compris une commande supposée en lecture seule), navigation, clavier, souris et MCP sont bloqués. Les commandes lancées manuellement par l'utilisateur dans l'onglet Terminal restent des actions de l'utilisateur.

Avec OpenCode, le message porte une règle de refus globale des outils, suivie uniquement des exceptions de lecture natives `read`, `glob`, `grep`, `list` lorsque les outils OpenCode sont activés. Les demandes d'autorisation restantes sont refusées en Plan. Les hooks/plugins exécutés par une installation externe OpenCode restent sous le contrôle de cette installation ; ce mode n'est pas un sandbox du processus externe.

## Orchestration

- **Disable** : aucun outil de délégation dans le moteur direct ; tout appel forgé est rejeté. L'outil `task` natif d'OpenCode est désactivé.
- **Auto** : le modèle décide d'appeler `delegate_tasks` pour des tâches indépendantes. Avec OpenCode en Exécution, son outil natif `task` peut être utilisé si les outils OpenCode sont activés.
- **Forced** : avant la réponse principale, deux sous-agents en lecture seule examinent respectivement les sources/conventions et les risques/critères de validation. Leurs rapports sont affichés et conservés dans la conversation. Le parent peut ensuite déléguer d'autres tâches avec le moteur direct.

Les sous-agents directs utilisent le même fournisseur et modèle que la conversation, avec un contexte séparé, les instructions du projet et les skills activés. Ils peuvent lire ou modifier les sources selon le mode hérité ; les analyses forcées restent en Plan. Ils n'ont pas d'outils terminal, bureau, navigateur ou MCP, et ne peuvent pas déléguer récursivement. Maximum trois tâches par appel, six sous-agents par envoi, huit étapes par sous-agent. Les rapports distinguent les limites atteintes des réponses terminées. Arrêter la conversation annule aussi ses sous-agents. Leurs appels consomment des tokens supplémentaires.

Les consultations forcées OpenCode utilisent des sessions distinctes et les outils natifs de lecture. Les sous-agents supplémentaires natifs OpenCode sont gérés par OpenCode ; ils n'utilisent pas la boucle `delegate_tasks` locale ni ses limites.

## Instructions du projet

Les fichiers **AGENTS.md** des racines associées et de leurs sous-dossiers sont chargés au début de chaque envoi. Les chemins exclus et les liens symboliques sont ignorés ou refusés. Chaque texte est accompagné de son dossier d'application ; les règles d'un sous-dossier prévalent pour ses fichiers. Les fichiers situés hors des racines associées ne sont pas parcourus.

Limites explicites : 32 Ko par fichier, 64 Ko au total, 2 000 dossiers parcourus par racine. Si une limite est atteinte, une erreur demande de réduire le périmètre au lieu de cacher une partie des instructions. Ces conventions ne peuvent pas modifier les autorisations ou le mode Plan.

## Dossier skills

Windows natif : `skills/` à côté de l'exécutable. Sur l'hôte Electron, il est placé à côté de `database.sqlite` (à côté de l'application distribuée ; dossier `.data` en développement). Le chemin exact apparaît dans **Réglages → Skills**.

Un exemple `skills/exemple-revue/SKILL.md` et sa ressource `resources/checklist.md` sont fournis sans remplacer vos personnalisations. Pour importer un skill, copiez son dossier dans `skills`, puis rouvrez les réglages ou le menu **+ → Skills**. Activez le skill souhaité : les nouveaux skills sont désactivés par défaut.

Chaque dossier doit contenir un `SKILL.md` avec ce frontmatter minimal (valeurs sur une ligne) :

```markdown
---
name: mon-skill
description: Quand et pourquoi utiliser ce skill.
---
# Instructions
Décrire ici les étapes et les critères de validation.
```

Le nom doit correspondre au dossier (minuscules, chiffres, tirets). Seules les descriptions des skills activés sont envoyées initialement. Le modèle charge le contenu avec `load_skill`, puis les ressources texte avec `read_skill_resource`. Aucun script n'est exécuté automatiquement. Les ressources restent limitées au dossier du skill. OpenCode peut lire le chemin explicite du `SKILL.md` avec son outil natif ; il conserve sa propre gestion des outils et permissions.
