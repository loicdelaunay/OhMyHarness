# MiniLM multilingue embarqué

Modèle : [sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2](https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2), français et anglais inclus. Export ONNX quantifié, CPU, pooling moyen et normalisation L2, 384 dimensions, fenêtre de 128 tokens comme Sentence Transformers.

Ressources épinglées :

- [Poids ONNX](https://huggingface.co/Xenova/paraphrase-multilingual-MiniLM-L12-v2/resolve/2c4055b12046f11709e9df2c122e59ffbdc2f900/onnx/model_quantized.onnx) : 118 308 126 octets.
- [Tokenizer SentencePiece](https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2/resolve/e8f8c211226b894fcb81acc59f3b34ba3efd5f42/sentencepiece.bpe.model).
- [Tokenizer JSON de référence](https://huggingface.co/Xenova/paraphrase-multilingual-MiniLM-L12-v2/resolve/2c4055b12046f11709e9df2c122e59ffbdc2f900/tokenizer.json), utilisé pour les tests de conformité des tokens.

SHA-256 :

- model.onnx : `66FC00F5F29AFCAFF34092E1BDD20008CA3918265A82FB9695A551E510CC4EBC`
- sentencepiece.bpe.model : `CFC8146ABE2A0488E9E2A0C56DE7952F7C11AB059ECA145A0A727AFCE0DB2865`

Licence Apache-2.0, copie dans LICENSE.txt. Microsoft.ML.Tokenizers applique la normalisation SentencePiece ; la casse et les accents ne sont pas supprimés. Les identifiants SentencePiece sont décalés pour le vocabulaire XLM-R du modèle (inconnu 3, début 0, fin 2).

Ces ressources sont embarquées dans OhMyHarness.Core puis extraites dans `models/minilm_multilingual` à côté de l'exécutable au premier usage. Aucun téléchargement à l'exécution. L'identifiant d'index est distinct de l'ancien modèle anglais : une réindexation du projet est nécessaire après la mise à jour.
