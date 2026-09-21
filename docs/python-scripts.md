# Skill Script Python

Activer **Script Python** dans **Réglages → Skills**, ou depuis le menu **+**.
Il fonctionne aussi sans dossier source associé.

- `python_info` : version, chemin de l’interpréteur embarqué, état d’extraction et scripts de la conversation.
- `write_python_script` : crée ou remplace un script `.py` après autorisation.
- `run_python_script` : exécute un script enregistré avec des arguments littéraux et renvoie `exit_code`, `stdout`, `stderr`, `timed_out`.

```json
{"path":"rapport.py","code":"from pathlib import Path\nPath('rapport.txt').write_text('Bonjour !', encoding='utf-8')\nprint('Rapport créé')"}
```

Puis, avec `run_python_script` :

```json
{"path":"rapport.py","args":[],"timeout_seconds":30}
```

Le répertoire de travail est le premier dossier source du projet, sinon celui des
scripts de la conversation. `working_directory` peut sélectionner un autre dossier
parmi les sources associées. Les imports entre scripts sont pris en charge. Le mode
isolé de Python ignore `PYTHONHOME`, `PYTHONPATH` et les packages utilisateur du PC ;
il ne constitue **pas une sandbox de sécurité**. Les scripts ont les droits de
l’utilisateur et peuvent accéder au système après autorisation.

Les demandes respectent Refuser tout / Demander / Acceptation automatique et les
autorisations mémorisées. En Plan, seule la consultation `python_info` est permise.
Ces outils locaux sont retirés en mode sandbox de conteneurs, sans repli local.
L’exécution n’accepte pas de saisie interactive. Délai par défaut : **30 s**, maximum
**600 s**. Annuler la conversation arrête l’exécution ; les sorties sont limitées
à 100 000 caractères chacune. Ne pas détacher de processus en arrière-plan.

## Publication portable

**CPython 3.13.15**, distribution [python-build-standalone](https://github.com/astral-sh/python-build-standalone/releases/tag/20260901),
est intégré comme ressource compressée dans le programme. Le téléchargement se fait
sur la machine de compilation, avec SHA-256 épinglé dans
`build/PythonRuntime.targets`. Aucun téléchargement ni Python installé ne sont
nécessaires sur la machine qui exécute l’application.

Les builds et publications WinUI/Service importent ce target automatiquement,
y compris un `dotnet publish` direct. Architectures : Windows x64/ARM64, macOS
Intel/Apple Silicon. Le premier build requiert Internet ; les suivants peuvent
réutiliser l’archive vérifiée dans `artifacts/python-cache`.

Au premier lancement autorisé d’un script, le runtime est extrait dans le profil
portable, à côté de l’EXE Windows (ou du profil fourni par l’hôte Electron) :

```text
OhMyHarness.App.exe
database.sqlite
skills/
runtimes/python/3.13.15-20260901-win-x64/python/...
scripts/python/chat-41/rapport.py
temp/python/chat-41/...
```

La bibliothèque standard, ses extensions natives et les licences de la distribution
sont conservées. Les bibliothèques métier telles que NumPy/Pandas ne sont pas
préinstallées. L’archive Windows x64 ajoute environ 47 Mo à la publication ; le
runtime extrait occupe davantage de place. Les anciennes versions extraites ne
sont pas supprimées automatiquement lors d’une mise à jour.

Validation portable : publier `tests/PythonRuntime.Probe` en EXE unique puis
l’exécuter depuis un autre répertoire. Ce test utilise le véritable interpréteur
embarqué et vérifie les autorisations, la portée des scripts, les imports, Unicode,
SQLite/SSL, les erreurs et le délai maximal.
