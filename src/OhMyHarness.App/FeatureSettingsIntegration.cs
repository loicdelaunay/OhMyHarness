using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    (StackPanel Browser, StackPanel Rag, StackPanel Vision, Func<string> Save) BuildFeatureSettings()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var browser = new StackPanel { Spacing=12 };
#if WINDOWS
        var browserLabel=WorkflowText("WebView2 intégré","Embedded WebView2");
#else
        var browserLabel=WorkflowText("Chromium isolé par conversation","Isolated Chromium per conversation");
        browser.Children.Add(Label(WorkflowText("Uno Desktop ouvre Chrome/Edge dans une fenêtre dédiée par conversation. Le DOM, JavaScript, clavier, souris et captures sont pilotés depuis l’application. Chrome/Edge est requis ; Node.js est uniquement nécessaire pour le mode MCP.","Uno Desktop uses a dedicated Chrome/Edge window per conversation with DOM, JavaScript, input and screenshots. Chrome/Edge is required; Node.js is only needed for MCP mode."),13));
#endif
        var mode = new ComboBox { Header=WorkflowText("Navigateur", "Browser"), ItemsSource=new[] { browserLabel, "Chrome · MCP", WorkflowText("Désactivé", "Disabled") }, SelectedIndex=config.BrowserMode=="chrome"?1:config.BrowserMode=="disabled"?2:0 };
        var executable = new TextBox { Header=WorkflowText("Chemin de Chrome (facultatif)", "Chrome path (optional)"), Text=config.ChromePath };
        browser.Children.Add(mode);browser.Children.Add(executable);
        browser.Children.Add(Label(WorkflowText("Le panneau Outils fonctionne sans navigateur. WebView2 démarre uniquement à la demande. En mode Chrome, l’IA utilise Chrome DevTools MCP dans une fenêtre externe, avec un profil par conversation. Chrome et Node.js doivent être installés. Les autorisations MCP restent applicables.", "Tools work without a browser. Chrome mode uses an external MCP browser with a per-conversation profile; Chrome and Node.js are required."),13));
        var rag = new StackPanel { Spacing=12 };
        var ragMode = new ComboBox { Header="Embeddings", ItemsSource=new[] { WorkflowText("MiniLM multilingue · CPU", "Multilingual MiniLM · CPU"), "OpenAI v1 API" }, SelectedIndex=config.RagMode=="api"?1:0 };
        var providers = db.Providers.Local.Where(x=>!x.IsOpenCode&&!x.IsComposite).ToList();
        var provider = new ComboBox { Header=WorkflowText("Fournisseur d’embeddings", "Embeddings provider"), ItemsSource=providers, SelectedItem=providers.FirstOrDefault(x=>x.Id==config.RagProviderId) ?? providers.FirstOrDefault() };
        var model = new TextBox { Header=WorkflowText("Modèle API", "API model"), Text=config.RagModel };
        var maxFiles = new NumberBox { Header=WorkflowText("Nombre maximum de fichiers", "Maximum files"), Minimum=1,Maximum=2000,Value=config.RagMaxFiles };
        var topK = new NumberBox { Header=WorkflowText("Nombre de résultats", "Results"), Minimum=1,Maximum=20,Value=config.RagTopK };
        foreach(var element in new Control[] {ragMode,provider,model,maxFiles,topK}) { element.HorizontalAlignment=HorizontalAlignment.Stretch; rag.Children.Add(element); }
        void RefreshApiFields() { provider.Visibility = model.Visibility = ragMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; }
        ragMode.SelectionChanged += (_, _) => RefreshApiFields(); RefreshApiFields();
        rag.Children.Add(Label(WorkflowText("Activez le skill RAG, puis demandez une indexation à l’IA. MiniLM multilingue (quantifié, ~118 Mo) fonctionne hors ligne en français et en anglais, avec recherche entre les langues. L’index reste dans SQLite. Réindexez vos sources après le passage de l’ancien modèle anglais au modèle multilingue, ou après modification des fichiers. En mode API, les textes sélectionnés sont transmis après autorisation.", "Enable RAG and ask the agent to index. Multilingual MiniLM (~118 MB) works offline in French and English, including cross-language search. Re-index after upgrading from the English model or editing sources. API transmission requires approval."),13));
        var targetProjectId=project?.Id;
        rag.Children.Add(Label(WorkflowText("Tous formats : extraction texte PDF/Office ; métadonnées et chaînes lisibles pour les binaires inconnus. Pas d’OCR pour les PDF scannés. Limites : 32 Mio et 500 000 caractères extraits par fichier.", "All formats: PDF/Office text extraction; metadata and printable strings for unknown binaries. No OCR for scanned PDFs. Limits: 32 MiB and 500,000 extracted characters per file."), 12));
        rag.Children.Add(Action(WorkflowText("Effacer l’index du projet", "Clear project index"), async()=> { if(targetProjectId!=null) {await using var context=new HarnessDb();await context.RagChunks.Where(x=>x.ProjectId==targetProjectId).ExecuteDeleteAsync();} }));
        var vision = new StackPanel { Spacing = 10 };
        var visionProvider = new ComboBox { Header = WorkflowText("Fournisseur vision", "Vision provider"), ItemsSource = providers,
            SelectedItem = providers.FirstOrDefault(x=>x.Id==config.VisionProviderId), HorizontalAlignment = HorizontalAlignment.Stretch };
        var visionModel = new ComboBox { Header = WorkflowText("Modèle vision", "Vision model"), IsEditable = true, Text = config.VisionModel, HorizontalAlignment = HorizontalAlignment.Stretch };
        void UpdateVisionModels() { visionModel.ItemsSource = ModelCatalog.GetModelsForProvider(visionProvider.SelectedItem as Provider); }
        visionProvider.SelectionChanged += (_, _) => { UpdateVisionModels(); visionModel.Text = (visionProvider.SelectedItem as Provider)?.Model ?? ""; };
        UpdateVisionModels();
        vision.Children.Add(visionProvider); vision.Children.Add(visionModel);
        vision.Children.Add(Action(WorkflowText("Charger les modèles", "Load models"), async()=>
        {
            if (visionProvider.SelectedItem is not Provider selected) return;
            visionModel.ItemsSource = await engine.ModelsAsync(selected, KeyVault.Decrypt(selected.ProtectedKey), CancellationToken.None);
        }));
        vision.Children.Add(Label(WorkflowText("Choisissez un modèle acceptant les images. Les images jointes et captures sont décrites automatiquement si le modèle principal n’a pas la vision. L’outil analyze_image permet une question ciblée, même avec un modèle principal vision. Les images sont envoyées au fournisseur choisi après autorisation et peuvent être facturées. Les descriptions peuvent être inexactes. OpenCode conserve ses propres outils ; ce relais y couvre seulement les images jointes.", "Choose an image-capable model. Attachments and screenshots are described automatically for non-vision main models. analyze_image supports focused questions even with a vision main model. Images are sent to the selected provider after approval and may incur charges. Descriptions can be inaccurate. OpenCode retains its own tools; this bridge only covers its attachments."),12));
        return (browser,rag,vision,()=>new FeatureSettings { BrowserMode=mode.SelectedIndex==1?"chrome":mode.SelectedIndex==2?"disabled":"embedded",ChromePath=executable.Text.Trim(),RagMode=ragMode.SelectedIndex==1?"api":"local",RagProviderId=(provider.SelectedItem as Provider)?.Id??0,RagModel=model.Text.Trim(),RagMaxFiles=(int)maxFiles.Value,RagTopK=(int)topK.Value,VisionProviderId=(visionProvider.SelectedItem as Provider)?.Id??0,VisionModel=visionModel.Text.Trim() }.Json());
    }
    readonly TextBlock browserNotice = new() { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(20), Text="Navigateur arrêté. Utilisez → pour démarrer. / Browser stopped. Use → to start." };
    void ShowBrowserNotice(string? error=null)
    {
        var mode=FeatureSettings.Read(state.FeaturesJson).BrowserMode;
        browserNotice.Text=error ?? (mode=="chrome" ? "Chrome externe : demandez à l’IA d’utiliser les outils Chrome DevTools MCP. / Ask the agent to use Chrome DevTools MCP." : mode=="disabled" ? "Navigateur désactivé / Browser disabled" : "Navigateur arrêté. Utilisez → pour démarrer. / Browser stopped. Use → to start.");
        if(!browserHost.Children.Contains(browserNotice))browserHost.Children.Add(browserNotice);
    }
}
