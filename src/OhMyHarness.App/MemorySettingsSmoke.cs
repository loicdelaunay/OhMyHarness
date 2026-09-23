using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeMemorySettingsAsync(FrameworkElement settingsRoot, string output)
    {
        static IEnumerable<FrameworkElement> Find(DependencyObject parent)
        {
            if (parent is FrameworkElement element) yield return element;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var descendant in Find(VisualTreeHelper.GetChild(parent, i))) yield return descendant;
        }
        static void Click(Button button) => (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new InvalidOperationException("Button invocation unavailable")).Invoke();
        var navigation = Find(settingsRoot).OfType<SettingsNavigation>().Single();
        navigation.SelectedIndex = 3;
        await Task.Delay(150);
        var memory = Find(settingsRoot).Single(x => Equals(x.Tag, "memory-settings"));
        Click(Find(memory).OfType<Button>().Single(x => x.Content?.ToString() == "Voir la mémoire"));
        for (var attempt = 0; attempt < 100 && !Find(memory).OfType<TextBlock>().Any(x => x.Text == "Aucune mémoire trouvée."); attempt++) await Task.Delay(20);
        if (!Find(memory).OfType<TextBlock>().Any(x => x.Text == "Aucune mémoire trouvée.")) throw new Exception("Memory viewer failed to load.");
        Click(Find(memory).OfType<Button>().Single(x => x.Content?.ToString() == "Créer une mémoire"));
        await Task.Delay(50);
        Find(memory).OfType<ComboBox>().Single(x => Equals(x.Tag, "memory-edit-scope")).SelectedIndex = 1;
        Find(memory).OfType<ComboBox>().Single(x => Equals(x.Tag, "memory-edit-category")).SelectedIndex = 2;
        Find(memory).OfType<TextBox>().Single(x => x.Header?.ToString() == "Clé stable").Text = "smoke.preference";
        var title = Find(memory).OfType<TextBox>().Single(x => x.Header?.ToString() == "Titre"); title.Text = "Préférence de réponse";
        var content = Find(memory).OfType<TextBox>().Single(x => x.Header?.ToString() == "Information mémorisée"); content.Text = "Des explications concises avec exemples.";
        var save = Find(memory).OfType<Button>().Single(x => x.Content?.ToString() == "Enregistrer la mémoire");
        var store = new MemoryStore(HarnessDb.DatabasePath); var access = new MemoryAccess(project!.Id, chat!.Id);
        Click(save);
        MemoryHit? saved = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            saved = (await store.SearchAsync(access, "smoke.preference")).Items.SingleOrDefault();
            if (saved != null && Find(memory).OfType<TextBlock>().Any(x => x.Text.Contains($"#{saved.Id} · v1"))) break;
            await Task.Delay(20);
        }
        if (saved == null || saved.Scope != "shared" || saved.Category != "user") throw new Exception("Memory editor did not save the requested scope/category.");
        content.Text = "Une préférence modifiée et persistante.";
        Click(save);
        for (var attempt = 0; attempt < 100 && (await store.ReadAsync(access, saved.Id)).Version != 2; attempt++) await Task.Delay(20);
        if ((await store.ReadAsync(access, saved.Id)).Version != 2) throw new Exception("Memory editor did not update the existing entry.");
        var search = Find(memory).OfType<TextBox>().Single(x => x.PlaceholderText?.StartsWith("Rechercher dans") == true);
        search.Text = "persist";
        await Task.Delay(450);
        if (!Find(memory).OfType<TextBlock>().Any(x => x.Text.Contains("[persistante]"))) throw new Exception("Memory UI did not display the indexed search excerpt.");
        Click(Find(memory).OfType<Button>().Single(x => x.Content?.ToString() == "Fermer"));
        search.StartBringIntoView();
        await Task.Delay(100);
        await Capture(settingsRoot, Path.Combine(output, "memory-settings.png"));
        await store.DeleteAsync(access, saved.Id, 2);
        navigation.SelectedIndex = 0;
        await Task.Delay(100);
    }
}
