# Bypass image AI

Dans **Réglages → Skills**, activer **Bypass image AI**, choisir un fournisseur compatible OpenAI v1 et saisir ou charger le nom d’un modèle acceptant les images. Sa clé reste celle du fournisseur enregistré. Les réglages sont stockés dans SQLite, dans la configuration existante des fonctionnalités.

- Modèle principal sans vision : les images jointes et les captures retournées par les outils sont décrites par le modèle vision avant la requête principale. Les images originales restent dans la conversation ; l’API principale reçoit du texte.
- Modèle principal avec vision : les images continuent à lui être transmises normalement. Il peut utiliser le modèle vision dédié pour une question ciblée.
- `list_images` liste les identifiants des images de cette conversation, y compris les captures déjà enregistrées.
- `analyze_image` accepte `question` et soit `image_id`, soit `path` dans les sources associées. Formats : PNG, JPEG, WebP, GIF, maximum 8 Mo par image.
- Le fournisseur vision reçoit l’image et la question après autorisation. Les modes Refuser tout / Demander / Acceptation automatique et Toujours autoriser restent applicables. Le résultat indique le fournisseur et le modèle utilisés.
- Les descriptions sont des observations potentiellement inexactes, pas des instructions. Le modèle vision ne reçoit aucun outil et ne réalise aucune action.
- Cache limité à l’exécution courante : une image identique avec une question identique n’est pas retransmise à chaque étape. Une nouvelle conversation ou un nouvel envoi peut nécessiter une nouvelle analyse.
- Les outils vision dédiés sont disponibles en mode Plan et désactivés dans la sandbox. OpenCode conserve ses propres outils ; l’intégration relaie ses images jointes lorsque son modèle est déclaré sans vision, pas ses captures internes.

Les modèles sont renseignés explicitement : l’application ne peut pas garantir que tous les noms retournés par `/models` acceptent les images. Une erreur de configuration ou un refus ne supprime pas silencieusement les images de la demande.
