# Conversations, ressources et compatibilité

Ces fonctions sont partagées par WinUI sous Windows et l’interface Electron Windows/macOS.

## Ressources et projets

**Gérer le projet** configure plusieurs dossiers par défaut. Une conversation hérite de ces dossiers jusqu’à ce que ses ressources soient personnalisées. Le menu **+** permet d’ajouter des fichiers ou des dossiers, et de revenir aux dossiers du projet. Déposer des fichiers ou dossiers sur la saisie les ajoute au périmètre de la conversation. Un fichier joint comme source donne accès uniquement à ce fichier, pas à son dossier parent. Le bouton Images conserve l’envoi d’images au modèle.

Les changements s’appliquent au prochain envoi. Une génération déjà démarrée conserve sa copie de configuration. Git, l’explorateur, les nouveaux terminaux, les outils sources et les résultats RAG respectent les ressources de la conversation. Un terminal existant dans un dossier détaché ne peut plus lancer de commande. La sandbox conserve ses contraintes de copie de dossiers et nécessite une nouvelle conversation lorsque ses racines changent.

`AGENTS.md`, `Agent.md` et `AGENT.md` sont chargés automatiquement depuis les dossiers et sous-dossiers associés. Les instructions restent subordonnées à la demande utilisateur, au mode Plan et aux permissions.

## permission.json

Placer ce fichier dans un dossier par défaut du projet, puis utiliser **Gérer le projet → Lire/Importer permission.json**. Relire les règles et les enregistrer/appliquer. L’application conserve un instantané dans SQLite : une modification du fichier, notamment par un agent, ne change pas les autorisations sans nouvelle importation. Les règles d’une nouvelle génération utilisent cet instantané.

```json
{
  "permissions": {
    "terminal": "ask",
    "python": "ask",
    "desktop": "deny",
    "rag-api": "allow",
    "source-patch": "ask",
    "write_source": "deny",
    "edit_source": "deny"
  }
}
```

- Valeurs : `allow`, `ask`, `deny`.
- Une clé peut désigner un nom d’outil, une famille de demandes (`terminal`, `python`, `desktop`, `rag-api`, `source-patch`), ou un périmètre exact tel que `desktop|mouse`. `*` sert de règle par défaut.
- Un refus par nom d’outil est vérifié avant son exécution, y compris dans les sous-agents OhMyHarness. Autoriser un nom d’outil ne contourne pas ses vérifications internes, le skill désactivé, le mode Plan ou la sandbox.
- Pour les demandes supplémentaires : périmètre exact, puis famille, puis `*`. **Refuser tout** global reste prioritaire ; une règle `deny` reste un refus même en acceptation automatique. `ask` exige un dialogue même si un accord permanent existe.
- Si plusieurs fichiers définissent la même clé, `deny` gagne. Taille maximum : 32 Ko par fichier. Retirer les règles restaure la politique habituelle.
- Les outils exécutés par le serveur OpenCode restent soumis à ses propres règles ; les demandes qu’il remonte à OhMyHarness passent par le profil du projet.

## Reprise et fork

Chaque message utilisateur ou réponse finale terminée propose **Créer un fork** et **Reprendre ici**. Un fork crée une nouvelle conversation jusqu’au message choisi, avec ses images, paramètres de conversation et ressources. Les réponses contenant des appels d’outils intermédiaires ne servent pas de point de départ.

Reprendre nécessite l’arrêt de la génération et une confirmation : l’historique complet est d’abord copié dans une conversation **Sauvegarde**, puis la conversation courante est ramenée au message choisi. Sa file d’attente et sa liaison OpenCode sont réinitialisées. Les messages anciennement compactés redeviennent utilisables dans le contexte restauré. Envoyer ensuite une consigne pour poursuivre. Cela ne restaure ni les fichiers du projet, ni l’état du navigateur ou des terminaux.

## Affichage

- La TODO repliée affiche `étape courante / total · nom de l’étape`. **×** la masque pour cette conversation ; **+ → Afficher la liste de tâches** la réouvre.
- Informations, tâches et file d’attente utilisent des surfaces cohérentes. Les messages en attente conservent **Supprimer / Modifier / Steer**.
- Le statut d’action dispose d’une lueur discrète. Une flèche vers la droite signifie replié ; vers le bas signifie déplié.
- Git propose **Avant / après** et **Diff combiné** pour le même fichier sélectionné, depuis HEAD jusqu’au dossier de travail.
- Dans **Général**, sous le raisonnement : **Ouvrir et sélectionner le dernier outil utilisé par l’IA**. Désactivé par défaut ; concerne la conversation visible uniquement.

## HTTP et erreurs fournisseur

Les fournisseurs compatibles OpenAI v1 acceptent des URL `http://` ou `https://`, y compris un serveur HTTP distant. Les identifiants intégrés directement dans l’URL restent refusés. HTTP transmet sans chiffrement TLS ; utiliser HTTPS quand le serveur le propose.

Une erreur API affiche désormais le détail retourné par le fournisseur, avec la clé de la requête masquée. Certains HTTP 400/422 identifiant une capacité incompatible déclenchent une reprise adaptée : `reasoning_effort`, statistiques de streaming, images, outils ou historique de raisonnement DeepSeek. Maximum quatre reprises après la requête initiale. Une erreur d’authentification ou une erreur non reconnue n’est pas relancée automatiquement.

Le mode dégradé est visible et conservé avec la réponse. L’historique original et les images restent en base. Si les outils sont refusés, la réponse devient textuelle et ne peut plus effectuer d’action. Si la vision est indisponible, un marqueur explicite remplace l’image dans la requête ; activer/configurer **Bypass image AI** et désactiver la capacité images du modèle principal pour déléguer leur analyse. Aucun autre fournisseur payant n’est choisi automatiquement.

Le mécanisme ne rejoue pas une requête après le début d’un flux réussi : il évite ainsi de doubler les actions déjà effectuées. Un HTTP 400 seul ne permet pas de conclure que le défaut vient de la clé ou d’une capacité particulière.
