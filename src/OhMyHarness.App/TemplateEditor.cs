using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using static OhMyHarness.App.UiText;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    sealed record TemplateEditorState(StackPanel Panel, List<PromptTemplate> Drafts, TextBlock Error);
    TemplateEditorState BuildTemplateEditor()
    {
        var drafts = db.Templates.Local.Where(x => db.Entry(x).State != EntityState.Deleted).OrderBy(x => x.Id)
            .Select(x => new PromptTemplate { Id = x.Id, Name = x.Name, Content = x.Content }).ToList();
        var chooser = new ComboBox { Header = "Template", HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = T("Nom du template"), MaxLength = 100 };
        var content = new TextBox { Header = T("Contenu du template"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 220, MaxHeight = 350, MaxLength = 50000 };
        var error = Label("", 12); error.Tag = null;
        PromptTemplate? selected = null; bool refreshing = false;
        void Select(PromptTemplate? item)
        {
            refreshing = true; selected = item;
            name.Text = item?.Name ?? ""; content.Text = item?.Content ?? "";
            name.IsEnabled = content.IsEnabled = item != null;
            refreshing = false;
        }
        void Refresh(PromptTemplate? item)
        {
            chooser.ItemsSource = null; chooser.ItemsSource = drafts.ToList(); chooser.SelectedItem = item; Select(item);
        }
        chooser.SelectionChanged += (_, _) => Select(chooser.SelectedItem as PromptTemplate);
        name.TextChanged += (_, _) => { if (!refreshing && selected != null) selected.Name = name.Text; };
        name.LostFocus += (_, _) => { if (!refreshing) Refresh(selected); };
        content.TextChanged += (_, _) => { if (!refreshing && selected != null) selected.Content = content.Text; };
        var add = new Button { Content = T("Nouveau template") };
        add.Click += (_, _) => { var item = new PromptTemplate { Name = T("Nouveau template"), Content = T("Décrivez votre demande ici.") }; drafts.Add(item); Refresh(item); };
        var remove = new Button { Content = T("Supprimer") };
        remove.Click += (_, _) => { if (selected == null) return; drafts.Remove(selected); Refresh(drafts.FirstOrDefault()); };
        var restore = new Button { Content = T("Réinitialiser Web app") };
        restore.Click += (_, _) =>
        {
            var item = drafts.FirstOrDefault(x => x.Id == 1) ?? drafts.FirstOrDefault(x => x.Name == "Web app");
            if (item == null) { item = new PromptTemplate { Name = "Web app" }; drafts.Add(item); }
            item.Content = DefaultTemplates.WebApp; Refresh(item);
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Label("Les templates préremplissent le message sans l’envoyer. Les modifications sont appliquées avec Enregistrer.", 12));
        foreach (var item in new UIElement[] { chooser, name, content, Row(add, remove), restore, error }) panel.Children.Add(item);
        Refresh(drafts.FirstOrDefault());
        return new(panel, drafts, error);
    }
    void SaveTemplateDrafts(List<PromptTemplate> drafts)
    {
        foreach (var existing in db.Templates.Local.ToList())
            if (!drafts.Any(x => x.Id == existing.Id)) db.Templates.Remove(existing);
        foreach (var draft in drafts)
        {
            var existing = draft.Id == 0 ? null : db.Templates.Local.FirstOrDefault(x => x.Id == draft.Id);
            if (existing == null) db.Templates.Add(new PromptTemplate { Name = draft.Name.Trim(), Content = draft.Content });
            else { existing.Name = draft.Name.Trim(); existing.Content = draft.Content; }
        }
    }
}
