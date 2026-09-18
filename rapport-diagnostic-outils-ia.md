# Rapport de diagnostic — lecture de fichiers et navigation web par l'IA

Date : 18/09/2026
Périmètre : OhMyHarness (WinUI 3 / .NET 10)
Objets : « le modèle dit qu'il ne peut pas lire un dossier source », « le modèle ne peut pas naviguer ni lire le navigateur ».

## 1. Résumé exécutif

Un modèle de langage n'a, par lui-même, aucun accès au disque ni au réseau. Il ne peut lire un dossier ou une page web **que si l'application lui déclare des outils (OpenAI *function calling*) et exécute ces appels à sa place**. Or dans OhMyHarness, ces outils sont conditionnels :

- `list_sources` / `read_source` ne sont envoyés au modèle que si **le projet a un dossier source associé** *et* si le skill « Exploration des sources » est actif (`MainWindow.cs:492`).
- `browse` / `read_page` ne sont envoyés que si **« Accès IA au navigateur » est activé** *et* si le skill « Recherche web » est actif (`MainWindow.cs:492`). Ce commutateur est **désactivé par défaut à chaque lancement** et n'est pas persisté (`MainWindow.cs:39`, README ligne 36).

Si l'une de ces conditions manque, le modèle reçoit la question sans aucun outil et répond — à juste titre — qu'il ne peut ni lire de fichier ni naviguer. C'est le comportement observé. S'y ajoute une incohérence : le prompt système lui **ordonne** d'utiliser ces outils même quand ils ne sont pas déclarés (`Skills.cs:8-9`, `MainWindow.cs:490`), ce qui produit des réponses confuses du type « je n'ai pas accès aux fichiers ».

## 2. Comment la fonctionnalité est censée marcher

1. L'application construit la requête `chat/completions` avec `tools` (JSON Schema des fonctions) — `ChatEngine.cs:112-133`, envoyé à `ChatEngine.cs:37`.
2. Le modèle répond en *streaming* soit du texte, soit un ou plusieurs `tool_calls` (assemblés depuis les fragments SSE — `ChatEngine.cs:72-82`).
3. L'application exécute l'outil demandé (`RunTool`, `MainWindow.cs:443-455`) puis renvoie le résultat au modèle (`role: "tool"`, `MainWindow.cs:524-530`), et reboucle jusqu'à 12 fois.

Autrement dit : les « skills » ne sont **pas** des capacités techniques données au modèle. Ce sont uniquement des instructions ajoutées au prompt (`Skills.cs:15-21`) et des verrous d'autorisation. La capacité réelle vient des *tools* et de leur exécution locale.

## 3. Causes racines identifiées

### C1 — Outils non déclarés (cause principale)

`ChatEngine.ToolDefinitions(sources, browser)` n'ajoute les outils que si les booléens sont vrais. `SendCoreAsync` les calcule ainsi :

```csharp
// MainWindow.cs:492
var definitions = ChatEngine.ToolDefinitions(
    !string.IsNullOrEmpty(project.SourceFolder) && Skills.Enabled(state.EnabledSkills, "sources"),
    browserAccess.IsOn && Skills.Enabled(state.EnabledSkills, "web"));
```

Conséquences :
- Dossier non associé au projet → aucun outil `list_sources` / `read_source`. Le modèle ne peut que répondre qu'il ne lit pas les fichiers.
- Navigateur non autorisé (par défaut au lancement) → aucun outil `browse` / `read_page`. Idem.

### C2 — Prompt incohérent avec les outils réellement disponibles

`Skills.Prompt` ajoute systématiquement les consignes des skills actifs :
- sources : « Inspect relevant project files before answering… » (`Skills.cs:8`) ;
- web : « Use the browser when current information is needed… » (`Skills.cs:9`).

Comme les deux skills sont actifs par défaut (`Storage.cs:64`), le modèle est instruit d'utiliser des outils… qui peuvent ne pas exister dans la requête. Il est donc poussé soit à halluciner, soit à répondre « je ne peux pas ».

### C3 — Aucune détection du support des outils par le modèle

Rien ne vérifie que le modèle choisi sait faire du *function calling*. Un modèle ou un serveur compatible qui ignore `tools` produira exactement le symptôme rapporté, sans message d'erreur explicite de l'application. Le README (ligne 72) indique d'ailleurs que les appels réels n'ont pas été exécutés pendant la création.

### C4 — Les erreurs d'outil sont renvoyées au modèle comme du texte

`RunTool` a un cas par défaut `"Outil non autorisé."` (`MainWindow.cs:454`) et le bloc appelant transforme les exceptions en `"Erreur outil : …"` (`MainWindow.cs:522`). Le modèle relaie souvent ce texte à l'utilisateur sous forme d'incapacité.

Points fragiles associés :
- Les arguments sont désérialisés **avant** le contrôle d'autorisation (`MainWindow.cs:446`). Des arguments vides (`""`) ou invalides, fréquents chez certains modèles, provoquent une erreur.
- L'outil `read_page` déclare un paramètre factice obligatoire `unused` (`ChatEngine.cs:130`) ; un modèle qui envoie `{}` ou `""` peut casser le parsing.
- Un `path` manquant sur `list_sources` provoque une `NullReferenceException` dans `RunTool`.

### C5 — Limitations fonctionnelles du navigateur

Même correctement autorisé, le navigateur est volontairement bridé : lecture seule, pas de clic ni de formulaire (README ligne 22), texte tronqué à 18 000 caractères et 60 liens (`MainWindow.cs:440`), sites dynamiques nécessitant une seconde lecture (README ligne 72), et navigation bloquée au-delà de 35 s (`MainWindow.cs:434`). Un modèle ne « navigue » pas : il demande l'ouverture d'une URL et lit un instantané du DOM.

## 4. Preuves relevées sur cette machine

Inspection de `%LOCALAPPDATA%\OhMyHarness\harness.db` (copie, WAL inclus) :

- `__EFMigrationsHistory` ne contient que `InitialCreate` : la base a été écrite par un binaire **antérieur au skill/langue** (`LanguageAndSkills` absente, colonnes `Language`/`EnabledSkills` inexistantes).
- `Projects.SourceFolder` = **vide** ; `Messages` = **0 ligne** ; le WAL ne contient aucun contenu de message (ni `read_source`, ni `assistant`).
- `Providers` : clé DeepSeek enregistrée (262 octets DPAPI), contexte réglé à 1 000 000, fournisseur actif = DeepSeek.

Inspection des binaires :

- `artifacts\win-x64\OhMyHarness.App.exe` (09:37) contient les outils (`list_sources`, `read_source`, `browse`, `read_page`, « Outil non autorisé », « Accès IA au navigateur ») mais **pas** le prompt des skills (`EnabledSkills` absent, « You are a project assistant » absent).
- `artifacts\release\win-x64\OhMyHarness.App.exe` (09:55) contient les skills et la migration 2.
- Les sources ont été modifiées après ces publications (`MainWindow.cs` 10:11, `UiText.cs` 10:12) ; le dernier build (`bin\Release\...\OhMyHarness.App.dll`, 10:17) n'a laissé aucune écriture dans la base.

Tests : `dotnet run --project tests/OhMyHarness.Tests -c Release` → **26/26 OK**. Mais aucun test n'exerce une vraie boucle d'outils : le test « Client HTTP complet » (`Program.cs:29-41`) se contente de vérifier le payload et de simuler un flux texte. Le maillon « le modèle demande un outil → l'app l'exécute → le modèle répond » n'est pas couvert.

Vérification fournisseur (documentation officielle DeepSeek) : `deepseek-flash` supporte les outils ; en mode *thinking*, l'API exige que `reasoning_content` soit renvoyé intégralement avec les requêtes contenant `tools` — ce que fait déjà `ChatEngine.cs:99` et `MainWindow.cs:530`. Sur ce point, pas d'incompatibilité.

## 5. Corrections recommandées (par priorité)

1. **Aligner le prompt sur les outils réellement déclarés.** Ne concaténer l'instruction d'un skill que si son outil est envoyé, et ajouter un état explicite (« Aucun dossier source n'est associé ; navigateur non autorisé »). C'est la correction qui supprime directement le symptôme `file: src/OhMyHarness.App/MainWindow.cs:490` + `src/OhMyHarness.Core/Skills.cs:15`.
2. **Rendre l'état « Accès IA au navigateur » visible/persistant** (ou l'indiquer dans le prompt) pour éviter que le modèle soit invité à naviguer alors que l'accès est coupé.
3. **Durcir `RunTool`** : parsing tolérant des arguments vides ou absents, validation avant usage, message d'erreur distinct d'un refus ; supprimer le paramètre factice `unused` de `read_page`.
4. **Détecter/avertir sur les capacités du modèle** (refus ou absence de `tool_calls`) et proposer un repli (par ex. injecter un fichier explicitement choisi par l'utilisateur).
5. **Ajouter un test de bout en bout** avec un faux fournisseur qui émet des `tool_calls` et vérifie l'exécution + le renvoi des résultats.
6. **Reconstruire et relancer la version courante** avant re-test (les sources actuelles sont postérieures aux EXE publiés).

## 6. Checklist de test immédiat

1. Zone de saisie → **« Sources »** : associer le dossier. Le libellé doit afficher « Sources partagées avec l'IA : <chemin> ».
2. Panneau navigateur → activer **« Accès IA au navigateur »** (à refaire à chaque lancement).
3. Réglages → **Skills** : vérifier que « Exploration des sources » et « Recherche web » sont activés.
4. Utiliser un modèle compatible *function calling* (`gpt-4.1-mini`, `deepseek-flash`).
5. Poser une question explicite : « Liste les fichiers du dossier source, puis lis le fichier principal. » / « Ouvre https://… et résume la page. »

## 7. Point à clarifier

La base locale ne contient **aucune conversation**. Le dernier binaire susceptible d'avoir écrit dedans est antérieur aux skills (09:37). Si les tests décrits ont été effectués sur une autre machine, une autre session ou une base supprimée entre-temps, le confirmer : les présentes conclusions reposent sur le code et sur cette base, tous deux cohérents avec « aucun outil déclaré → le modèle refuse ».
