# Tâches, questions et protection contre les boucles

Disponibles dans WinUI sous Windows et dans l'interface Electron sous Windows/macOS.

## Liste de tâches

L'agent dispose de `todowrite` pour remplacer la liste structurée de la conversation. Les statuts sont **À faire**, **En cours**, **Terminée** et **Annulée**. La carte se met à jour et indique le nombre de tâches terminées. Elle peut être repliée. La liste est enregistrée dans `database.sqlite`, relue après réouverture et incluse dans le contexte au prochain envoi. L'agent décide quand créer et mettre à jour ses étapes ; l'application ne transforme pas automatiquement tout texte de plan en tâches.

La liste utilise une ligne `Message` avec `Role=tasks`, `State=ui`, distincte de l'historique transmis au fournisseur. Aucun changement du schéma SQLite n'est nécessaire. Les résultats de `todowrite` restent dans l'historique normal des outils. Les sous-agents directs ne remplacent pas la liste du parent.

## Questions interactives

L'outil `question` propose de 1 à 8 questions, des choix simples ou multiples et/ou une réponse libre. La réponse n'est envoyée qu'après validation du formulaire. Une sélection n'est jamais faite à la place de l'utilisateur. Le mode Plan autorise les questions et la liste de tâches.

Seul l'agent qui attend la réponse est suspendu : les autres conversations et sous-agents peuvent continuer. Les formulaires restent dans leur conversation, sans bloquer l'ouverture des réglages. Changer de conversation conserve les réponses en cours de saisie. Les réponses et annulations sont conservées dans SQLite ; arrêter la génération retire ses questions en attente. Un formulaire annulé renvoie explicitement une annulation au modèle, jamais une approbation. Les formulaires non soumis sont abandonnés à la fermeture de l'application, comme les générations en cours.

Les questions ne sont pas des autorisations d'accès : **Refuser tout**, **Demander**, **Acceptation automatique** et les autorisations mémorisées ne répondent pas à ces formulaires. Les outils privilégiés conservent leurs propres contrôles.

## Appels répétés

Avant le **troisième appel consécutif** au même outil avec les mêmes arguments, l'application suspend ce travail et propose **Arrêter** ou **Continuer une fois**. L'ordre des propriétés JSON et les espaces de mise en forme n'évitent pas la détection. Un appel différent réinitialise la série. Continuer n'autorise que cet appel ; le suivant identique redemande une décision. La série est conservée pendant la compaction et l'auto-continue, et isolée par conversation/sous-agent. Il ne s'agit pas d'une détection sémantique de toutes les boucles possibles (par exemple alternance de deux outils).

## OpenCode

Avec les outils OpenCode activés, les questions sont relayées depuis `/question` et les tâches depuis `/session/{id}/todo`. Seules les demandes de la session concernée sont traitées. Les réponses passent par `reply` ou `reject`. La session reçoit une règle explicite `doom_loop: ask` ; ces décisions passent par le formulaire, même avec acceptation automatique. Arrêter cette boucle interrompt la session distante. Le relais attend l'état inactif de la session afin de ne pas confondre la fin d'un appel de modèle intermédiaire avec la fin du travail.

Le serveur doit prendre en charge ces routes et la mise à jour des permissions de session. Une erreur de relais interrompt le suivi et demande l'arrêt de la session ; elle n'est pas transformée en accord implicite. La détection des appels natifs est réalisée par OpenCode lui-même. Les sessions de consultation créées par l'application disposent du même relais de questions, sans remplacer la liste de tâches du parent. Les sous-agents natifs créés par OpenCode conservent sa propre gestion.

Références : [outils OpenCode](https://opencode.ai/docs/tools/), [permissions OpenCode](https://opencode.ai/docs/permissions/).
