# Terminaux multiples

Le panneau **Terminal** contient désormais plusieurs onglets, propres à chaque conversation. **+** ouvre un terminal local et **×** le ferme en arrêtant sa commande. Chaque onglet conserve son nom, son état, son brouillon et la sortie de sa dernière commande pendant la session de l'application. Changer de conversation ou ouvrir les paramètres n'arrête pas les commandes.

Les sorties locales sont actualisées pendant l'exécution. Les terminaux sandbox de l'agent apparaissent dans le même panneau avec la mention **Sandbox** ; leur sortie est disponible après le retour du conteneur. Ils peuvent être arrêtés ou fermés depuis le panneau. Leur lancement reste réservé à l'agent sandbox afin de conserver son espace isolé.

## Outils du skill Terminal

| Outil | Fonction |
|---|---|
| `list_terminals` | Lister les terminaux de la conversation et du mode courant, leur shell, leur état et leur dernière sortie. |
| `create_terminal` | Créer un onglet nommé dans le dossier source du projet. |
| `start_terminal` | Demander l'autorisation, lancer une commande et retourner immédiatement son `jobId`. |
| `read_terminal` | Lire la sortie disponible sans attendre. |
| `wait_terminal` | Attendre de façon asynchrone au plus `timeout_ms`, puis retourner l'état et la sortie ; 10 s par défaut, 30 s maximum. |
| `stop_terminal` | Arrêter la commande sans fermer l'onglet. |
| `delete_terminal` | Arrêter la commande et supprimer l'onglet. |

Les outils utilisent `terminal_id`. Pour lire ou attendre une commande précise, fournissez également `job_id`, avec la valeur `jobId` renvoyée au lancement. Les dix dernières commandes restent consultables par identifiant. `run_terminal` est conservé pour compatibilité : il utilise également un onglet, mais attend sa commande.

L'agent peut démarrer une commande dans A, en démarrer une autre dans B, puis consulter ou attendre leurs résultats. Une attente n'occupe pas le verrou global des outils. Les attentes d'au moins une seconde ne déclenchent pas la protection contre les appels identiques ; les interrogations immédiates répétées restent contrôlées.

## Limites et portée

- Les commandes sont **non interactives**, avec un nouveau processus PowerShell sous Windows ou zsh sous macOS. Elles ne constituent pas un shell persistant : variables, `cd` et sessions interactives ne sont pas conservés entre commandes. Regroupez les opérations liées dans une même commande.
- Une commande active par onglet, 12 onglets par conversation, 64 au total et 16 commandes simultanées. Chaque commande conserve la limite actuelle de 60 secondes et une sortie plafonnée à environ 100 000 caractères.
- `completed` indique que le processus s'est terminé ; son code de sortie figure dans le résultat. `failed`, `cancelled` et `timed_out` signalent les autres issues.
- Les permissions restent demandées au lancement, selon le réglage global. Le mode Plan peut lister, lire et attendre ; il ne peut pas lancer de commande.
- Un agent ne peut pas manipuler un terminal d'une autre conversation ni utiliser un terminal local depuis une sandbox. Les terminaux pointant vers un dossier détaché ne peuvent plus être lancés.
- En sandbox, les commandes parallèles utilisent des copies distinctes. Seuls leurs fichiers modifiés sont réintégrés dans la copie de la conversation, après vérification des contenus d'origine. Un conflit est signalé et la copie de récupération conservée. L'application au projet réel nécessite toujours la revue sandbox. Les commandes sandbox sont arrêtées à la fin de la génération avant la libération de l'espace isolé.
- Fermer l'application ou supprimer une conversation arrête ses commandes. Les onglets ne sont pas restaurés après redémarrage.
