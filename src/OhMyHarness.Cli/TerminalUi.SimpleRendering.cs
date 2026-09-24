using System.Text.Json;
using System.Text.Json.Nodes;
using OhMyHarness.Core;

namespace OhMyHarness.Cli;

public sealed partial class TerminalUi
{
    TerminalCanvas Draw(int width, int height)
    {
        var settings = FeatureSettings.Read(workspace?.State.FeaturesJson ?? "{}");
        var appearance = CliThemes.Resolve(dialog?.ThemePreview ?? options.Theme ?? settings.CliTheme, settings.Theme);
        var p = appearance.Palette;
        var canvas = new TerminalCanvas(width, height, p.Normal, appearance.Border);
        queueRows.Clear();
        if (width < 56 || height < 18)
        {
            canvas.Write(1, 1, "OhMyHarness CLI", p.Highlight);
            canvas.Write(1, 3, L("Agrandissez le terminal (56 × 18 minimum).", "Enlarge the terminal (56 × 18 minimum)."), p.Dim);
            canvas.Write(1, 5, "Ctrl+Q · " + L("Quitter", "Quit"), p.Dim); return canvas;
        }
        const int left = 2, transcriptTop = 6;
        int bodyWidth = Math.Min(width - 4, 120);
        Header(canvas, appearance, left, bodyWidth);
        if (dialog != null) { DrawDialog(canvas, p, dialog, appearance); return canvas; }
        var view = View(chatId);
        int inputRows = Math.Clamp(TerminalText.Wrap(editor.Text + " ", bodyWidth - 4).Count, 1, 5);
        int inputTop = height - inputRows - 4, statusTop = inputTop - 2;
        var suggestions = completion.Matches(editor);
        int suggestionRows = Math.Min(suggestions.Count, Math.Min(5, Math.Max(1, statusTop - transcriptTop - 3)));
        int suggestionTop = statusTop - suggestionRows - 2;
        int timelineHeight = Math.Max(1, (suggestionRows > 0 ? suggestionTop : statusTop) - transcriptTop - 1);
        var lines = Transcript(view, bodyWidth - 4);
        if (view.Scroll > 0 && view.LastWidth == bodyWidth) view.Scroll += Math.Max(0, lines.Count - view.LastLineCount);
        view.LastLineCount = lines.Count; view.LastWidth = bodyWidth;
        if (lines.Count == 0 && suggestionRows == 0)
        {
            canvas.Write(left, transcriptTop + 1, L("Que souhaitez-vous créer ?", "What would you like to build?"), p.Normal, bodyWidth);
            canvas.Write(left, transcriptTop + 3, "/connect   " + L("Connecter un fournisseur", "Connect a provider"), p.Dim, bodyWidth);
            canvas.Write(left, transcriptTop + 4, "/help      " + L("Commandes et raccourcis", "Commands and shortcuts"), p.Dim, bodyWidth);
        }
        else
        {
            view.Scroll = Math.Clamp(view.Scroll, 0, Math.Max(0, lines.Count - timelineHeight));
            int start = Math.Max(0, lines.Count - timelineHeight - view.Scroll);
            for (int row = 0; row < timelineHeight && row + start < lines.Count; row++)
            {
                var line = lines[row + start];
                var ink = new Ink(line.Kind switch { "user-title" or "user" or "heading" => p.Accent, "tool-title" or "task" => p.Green, "code" => p.Gold, "muted" or "tool" => p.Muted, _ => p.Foreground }, p.Background,
                    line.Kind is "user-title" or "heading" or "tool-title");
                canvas.Write(left, transcriptTop + row, line.Text, ink, bodyWidth - 2);
                if (line.QueueId > 0)
                {
                    queueButtonsStart = left + bodyWidth - 14;
                    canvas.Write(queueButtonsStart, transcriptTop + row, "[✎] [×] [↗]", p.Highlight, 12);
                    queueRows[transcriptTop + row] = (chatId, line.QueueId);
                }
            }
        }
        if (view.Loading) canvas.Write(left, transcriptTop, L("Chargement de l’historique…", "Loading history…"), p.Dim, bodyWidth);
        if (view.Messages.Values.LastOrDefault(m => m.Role == "tasks") is { } checklist)
        {
            try
            {
                var todos = JsonNode.Parse(checklist.Content)!.AsArray();
                var current = todos.FirstOrDefault(t => S(t, "status") == "in_progress");
                if (current != null) canvas.Write(left, statusTop - 1, $"◉ {todos.Count(t => S(t, "status") == "completed")}/{todos.Count} · {S(current, "content")}", new(p.Green, p.Background), bodyWidth);
            }
            catch (JsonException) { }
        }
        string status = connectionProgress.Length > 0 ? connectionProgress : view.Status.Length > 0 ? view.Status : notice;
        bool busy = Running(chatId) || pendingOperations > 0;
        canvas.Write(left, statusTop, (busy ? "◐◓◑◒"[(frame / 3) % 4] + " " : "") + status, busy ? p.Highlight : p.Dim, bodyWidth);
        var pendingImages = attachments.GetValueOrDefault(chatId, []);
        if (pendingImages.Count > 0) canvas.Write(left, inputTop - 1, "▧ " + string.Join(" · ", pendingImages.Select(a => a.Name)), p.Highlight, bodyWidth);
        else if (view.Scroll > 0) canvas.Write(left, inputTop - 1, L("Historique · Fin pour suivre la réponse", "History · End to follow the response"), p.Dim, bodyWidth);
        if (suggestionRows > 0)
        {
            int firstSuggestion = Math.Max(0, completion.Selected - suggestionRows + 1);
            for (int i = 0; i < suggestionRows; i++)
            {
                var suggestion = suggestions[firstSuggestion + i];
                bool selected = firstSuggestion + i == completion.Selected;
                canvas.Write(left, suggestionTop + i, (selected ? "> " : "  ") + suggestion.Value, selected ? p.Highlight : p.Normal, 18);
                canvas.Write(left + 19, suggestionTop + i, suggestion.Label, p.Dim, bodyWidth - 19);
            }
            canvas.Write(left, suggestionTop + suggestionRows, $"{completion.Selected + 1}/{suggestions.Count} · " + L("↑↓ choisir · Tab/Entrée compléter · Échap fermer", "↑↓ choose · Tab/Enter complete · Esc close"), p.Dim, bodyWidth);
        }
        var rule = new string(appearance.Border == TerminalBorder.Ascii ? '-' : '─', bodyWidth);
        canvas.Write(left, inputTop, rule, p.Dim);
        canvas.Write(left, inputTop + inputRows + 1, rule, p.Dim);
        canvas.Write(left, inputTop + 1, ">", p.Highlight);
        InputRendering.Draw(canvas, editor, left + 2, inputTop + 1, bodyWidth - 4, inputRows, p, appearance.Crt);
        int limit = view.Limit > 0 ? view.Limit : CurrentProvider?.ContextLimit ?? 0;
        string context = limit > 0 ? $"{100.0 * view.Tokens / limit:0.#}%" : "—";
        var model = CurrentProvider?.Model ?? "/connect";
        string summary = $"{model} · {view.Speed:0.#} tok/s · {context}";
        int summaryWidth = Math.Min(TerminalText.Width(summary), bodyWidth - 24);
        canvas.Write(left, height - 1, "/help · Ctrl+P", p.Dim, bodyWidth - summaryWidth - 2);
        canvas.Write(left + bodyWidth - summaryWidth, height - 1, summary, p.Dim, summaryWidth);
        return canvas;
    }

    void Header(TerminalCanvas canvas, CliTheme appearance, int left, int width)
    {
        var p = appearance.Palette;
        canvas.Write(left, 1, "◈", new(p.Accent, p.Background, true));
        canvas.Write(left + 4, 1, "OhMyHarness CLI 1.8.0", p.Highlight, width - 4);
        if (appearance.Crt) canvas.Write(left + 29, 1, appearance.Brand, p.Dim, width - 29);
        var thinking = workspace?.State.ThinkingLevel ?? "auto";
        canvas.Write(left + 4, 2, (CurrentProvider == null ? L("Aucun fournisseur · /connect", "No provider · /connect") : CurrentProvider.Name + " · " + CurrentProvider.Model) + " (" + thinking + ")", p.Dim, width - 4);
        canvas.Write(left + 4, 3, CurrentProject?.GetSourceFolders().FirstOrDefault() ?? Environment.CurrentDirectory, p.Dim, width - 4);
        int active = runs.Count(r => !r.Value.IsCompleted);
        if (active > 0) canvas.Write(left + 4, 4, $"{active} " + L("conversation(s) active(s) · /chats", "active conversation(s) · /chats"), new(p.Green, p.Background), width - 4);
        else if (availableUpdate != null) canvas.Write(left + 4, 4, L("Mise à jour ", "Update ") + availableUpdate.Version + " · /update", p.Highlight, width - 4);
    }

    void DrawDialog(TerminalCanvas canvas, Palette p, UiDialog prompt, CliTheme appearance)
    {
        const int x = 2, y = 6;
        int width = Math.Min(canvas.Width - 4, 120), bottom = canvas.Height - 2;
        canvas.Write(x, y, appearance.Heading(prompt.Title), p.Highlight, width);
        int bodyRows = prompt.Body.Length == 0 ? 0 : Math.Min(prompt.Choices == null ? 5 : 4, Math.Max(1, bottom - y - 7));
        var body = TerminalText.Wrap(prompt.Body, width); prompt.Scroll = Math.Clamp(prompt.Scroll, 0, Math.Max(0, body.Count - bodyRows));
        for (int i = 0; i < bodyRows && i + prompt.Scroll < body.Count; i++) canvas.Write(x, y + 2 + i, body[i + prompt.Scroll], p.Dim, width);
        int inputY = y + bodyRows + 2;
        canvas.Write(x, inputY, "> ", p.Normal);
        InputRendering.Draw(canvas, prompt.Input, x + 2, inputY, width - 4, 1, p, appearance.Crt, prompt.Secret);
        int firstRow = inputY + 2;
        if (prompt.Choices != null)
        {
            var choices = prompt.Filtered;
            int available = Math.Max(1, bottom - firstRow - 1);
            prompt.Selected = Math.Clamp(prompt.Selected, 0, Math.Max(0, choices.Count - 1));
            int first = Math.Max(0, prompt.Selected - available + 1);
            bool commands = choices.Count > 0 && choices.All(c => c.Value.StartsWith('/'));
            int commandWidth = Math.Min(22, width / 3);
            for (int i = 0; i < available && i + first < choices.Count; i++)
            {
                var choice = choices[i + first]; bool selected = prompt.Selected == i + first;
                canvas.Write(x, firstRow + i, selected ? ">" : " ", p.Highlight);
                if (commands)
                {
                    canvas.Write(x + 2, firstRow + i, choice.Value, new(selected ? p.Accent : p.Green, p.Background, selected), commandWidth - 2);
                    canvas.Write(x + commandWidth, firstRow + i, choice.Label + (choice.Detail.Length == 0 ? "" : " · " + choice.Detail), selected ? p.Normal : p.Dim, width - commandWidth);
                }
                else canvas.Write(x + 2, firstRow + i, choice.Label + (choice.Detail.Length == 0 ? "" : " · " + choice.Detail), selected ? p.Highlight : p.Normal, width - 2);
            }
            if (choices.Count == 0) canvas.Write(x, firstRow, L("Aucun résultat", "No results"), p.Dim, width);
            if (choices.Count > available) canvas.Write(x, bottom - 1, $"{prompt.Selected + 1}/{choices.Count}", p.Dim, width);
        }
        canvas.Write(x, canvas.Height - 1, L("↑/↓ choisir · Entrée valider · Échap annuler · PgUp/PgDn lire", "↑/↓ select · Enter confirm · Esc cancel · PgUp/PgDn read"), p.Dim, width);
    }
}
