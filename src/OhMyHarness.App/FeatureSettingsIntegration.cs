using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    (StackPanel Browser, StackPanel Rag, Func<string> Save) BuildFeatureSettings()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var browser = new StackPanel { Spacing=12 };
        var mode = new ComboBox { Header="Navigateur / Browser", ItemsSource=new[] { "WebView2 intégré / Embedded", "Chrome · MCP", "Désactivé / Disabled" }, SelectedIndex=config.BrowserMode=="chrome"?1:config.BrowserMode=="disabled"?2:0 };
        var executable = new TextBox { Header="Chrome : chemin facultatif / Optional executable path", Text=config.ChromePath };
        browser.Children.Add(mode);browser.Children.Add(executable);
        browser.Children.Add(Label("Le panneau Outils fonctionne sans navigateur. WebView2 démarre uniquement à la demande. En mode Chrome, l’IA utilise Chrome DevTools MCP dans une fenêtre externe, avec un profil par conversation. Chrome et Node.js doivent être installés. Les autorisations MCP restent applicables. / Tools work without a browser. Chrome mode uses an external MCP browser with a per-conversation profile; Chrome and Node.js are required.",13));
        var rag = new StackPanel { Spacing=12 };
        var ragMode = new ComboBox { Header="Embeddings", ItemsSource=new[] { "MiniLM multilingue / Multilingual · CPU", "OpenAI v1 API" }, SelectedIndex=config.RagMode=="api"?1:0 };
        var providers = db.Providers.Local.Where(x=>!x.IsOpenCode&&!x.IsComposite).ToList();
        var provider = new ComboBox { Header="Fournisseur embeddings / Embeddings provider", ItemsSource=providers, SelectedItem=providers.FirstOrDefault(x=>x.Id==config.RagProviderId) ?? providers.FirstOrDefault() };
        var model = new TextBox { Header="Modèle API / API model", Text=config.RagModel };
        var maxFiles = new NumberBox { Header="Fichiers maximum / Maximum files", Minimum=1,Maximum=2000,Value=config.RagMaxFiles };
        var topK = new NumberBox { Header="Résultats / Results", Minimum=1,Maximum=20,Value=config.RagTopK };
        foreach(var element in new UIElement[] {ragMode,provider,model,maxFiles,topK})rag.Children.Add(element);
        rag.Children.Add(Label("Activez le skill RAG, puis demandez une indexation à l’IA. MiniLM multilingue (quantifié, ~118 Mo) fonctionne hors ligne en français et en anglais, avec recherche entre les langues. L’index reste dans SQLite. Réindexez vos sources après le passage de l’ancien modèle anglais au modèle multilingue, ou après modification des fichiers. En mode API, les textes sélectionnés sont transmis après autorisation. / Enable RAG and ask the agent to index. Multilingual MiniLM (~118 MB) works offline in French and English, including cross-language search. Re-index after upgrading from the English model or editing sources. API transmission requires approval.",13));
        var targetProjectId=project?.Id;
        rag.Children.Add(Action("Effacer l’index du projet / Clear project index", async()=> { if(targetProjectId!=null) {await using var context=new HarnessDb();await context.RagChunks.Where(x=>x.ProjectId==targetProjectId).ExecuteDeleteAsync();} }));
        return (browser,rag,()=>new FeatureSettings { BrowserMode=mode.SelectedIndex==1?"chrome":mode.SelectedIndex==2?"disabled":"embedded",ChromePath=executable.Text.Trim(),RagMode=ragMode.SelectedIndex==1?"api":"local",RagProviderId=(provider.SelectedItem as Provider)?.Id??0,RagModel=model.Text.Trim(),RagMaxFiles=(int)maxFiles.Value,RagTopK=(int)topK.Value }.Json());
    }
    readonly TextBlock browserNotice = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(20), Text="Navigateur arrêté. Utilisez → pour démarrer. / Browser stopped. Use → to start." };
    void ShowBrowserNotice(string? error=null)
    {
        var mode=FeatureSettings.Read(state.FeaturesJson).BrowserMode;
        browserNotice.Text=error ?? (mode=="chrome" ? "Chrome externe : demandez à l’IA d’utiliser les outils Chrome DevTools MCP. / Ask the agent to use Chrome DevTools MCP." : mode=="disabled" ? "Navigateur désactivé / Browser disabled" : "Navigateur arrêté. Utilisez → pour démarrer. / Browser stopped. Use → to start.");
        if(!browserHost.Children.Contains(browserNotice))browserHost.Children.Add(browserNotice);
    }
}
