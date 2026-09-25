using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OhMyHarness.Core;
using System.Text.Json.Nodes;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    readonly ComboBox assetPicker = new() { HorizontalAlignment=HorizontalAlignment.Stretch, MinWidth=160 };
    readonly Image assetImage = new() { Stretch=Stretch.Uniform, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center };
    readonly StackPanel assetLayers = new() { Spacing=5 };
    readonly TextBlock assetStatus = new() { TextWrapping=TextWrapping.Wrap, FontSize=12 };
    readonly TextBlock assetEmpty = new() { TextWrapping=TextWrapping.Wrap, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center,
        MaxWidth=320 };
    readonly ProgressBar assetLoading = new() { IsIndeterminate=true, Height=3, Visibility=Visibility.Collapsed };
    readonly ComboBox assetFormat = new() { ItemsSource = new[] { "SVG", "PNG", "WebP", "JPEG", "PDF" }, SelectedIndex=0, MinWidth=80 };
    readonly CheckBox assetTransparent = new() { IsChecked=true };
    readonly TextBox assetExportBackground = new() { Text="#FFFFFF", PlaceholderText="#RRGGBB", Width=105 };
    readonly ComboBox assetScale = new() { ItemsSource=new[] { "1×", "2×", "4×" }, SelectedIndex=0, Width=75 };
    AssetDocument? selectedAsset;
    int assetLoadRevision;
    bool populatingAssets;
    AssetWorkspace CurrentAssets() => new(db.Database.GetDbConnection().DataSource,chat?.Id ?? throw new InvalidOperationException("Choisissez une conversation."));
    Grid BuildAssetsPane()
    {
        assetPicker.PlaceholderText=WorkflowText("Choisir un asset","Choose an asset");
        assetEmpty.Text=WorkflowText("Activez le skill Générateur d’assets, puis décrivez votre dessin à l’IA.","Enable Asset generator, then describe your drawing to the AI.");
        assetTransparent.Content=WorkflowText("Fond transparent","Transparent background");
        var pane = new Grid { RowSpacing=8, Padding=new(4,8,4,4) };
        foreach(var h in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1,GridUnitType.Star), GridLength.Auto, GridLength.Auto }) pane.RowDefinitions.Add(new() { Height=h });
        var header = new Grid { ColumnSpacing=6 };
        header.ColumnDefinitions.Add(new() { Width=new(1,GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width=GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        header.Children.Add(assetPicker);
        var create = Action("+",CreateAssetManuallyAsync); ToolTipService.SetToolTip(create,WorkflowText("Nouveau canevas","New canvas"));
        Grid.SetColumn(create,1);header.Children.Add(create);
        var refresh = Action("↻",()=>RefreshAssetsAsync());ToolTipService.SetToolTip(refresh,WorkflowText("Actualiser","Refresh"));Grid.SetColumn(refresh,2);header.Children.Add(refresh);
        pane.Children.Add(header);
        var exportButton = new Button { Content=WorkflowText("Exporter…","Export…") };
        var exportOptions = new StackPanel { Spacing=8, Width=260 };
        exportOptions.Children.Add(Label("Format",12));exportOptions.Children.Add(assetFormat);
        exportOptions.Children.Add(assetTransparent);
        exportOptions.Children.Add(Row(Label(WorkflowText("Fond","Background"),12),assetExportBackground));
        exportOptions.Children.Add(Row(Label(WorkflowText("Échelle","Scale"),12),assetScale));
        exportOptions.Children.Add(Action(WorkflowText("Enregistrer sous…","Save as…"),SaveAssetAsAsync));
        exportButton.Flyout = new Flyout { Content=exportOptions };
        assetFormat.SelectionChanged += (_,_) => { if(assetFormat.SelectedIndex==3)assetTransparent.IsChecked=false; assetTransparent.IsEnabled=assetFormat.SelectedIndex!=3; };
        var paletteButton = new Button { Content="◉ Palette" };
        var palette = new ColorPicker { IsAlphaEnabled=true, IsHexInputVisible=true, IsColorSpectrumVisible=true, IsColorSliderVisible=true };
        var colorText = new TextBox { Text="#4CC9F0", IsReadOnly=true };
        var colorPanel = new StackPanel { Spacing=8, Width=300 }; colorPanel.Children.Add(palette);colorPanel.Children.Add(colorText);
        palette.ColorChanged += (_,e) => colorText.Text=$"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}{e.NewColor.A:X2}";
        colorPanel.Children.Add(Action(WorkflowText("Appliquer au fond","Set background"),async()=> {
            if(selectedAsset is not {} doc)return;
            var updated=await CurrentAssets().UpdateAsync(doc.Id,doc.Revision,d=>d.Background=colorText.Text,CancellationToken.None);
            await RefreshAssetsAsync(updated.Id);
        }));
        colorPanel.Children.Add(Action(WorkflowText("Retirer le fond","Clear background"),async()=> {
            if(selectedAsset is not {} doc)return;
            var updated=await CurrentAssets().UpdateAsync(doc.Id,doc.Revision,d=>d.Background="none",CancellationToken.None);
            await RefreshAssetsAsync(updated.Id);
        }));
        paletteButton.Flyout=new Flyout { Content=colorPanel };
        var toolbar=Row(paletteButton,exportButton);Grid.SetRow(toolbar,1);pane.Children.Add(toolbar);
        var canvas = new Grid { MinHeight=180, Background=FluentDesign.Resource("SolidBackgroundFillColorBaseBrush") };
        canvas.Children.Add(assetImage);canvas.Children.Add(assetEmpty);
        var frame=new Border { Child=canvas, Padding=new(12), CornerRadius=new(12), BorderThickness=new(1), BorderBrush=FluentDesign.Stroke };
        Grid.SetRow(frame,2);pane.Children.Add(frame);
        var layerSection = new Expander { Header=WorkflowText("Calques · premier plan en haut","Layers · front first"), IsExpanded=true, HorizontalAlignment=HorizontalAlignment.Stretch, HorizontalContentAlignment=HorizontalAlignment.Stretch,
            Content=new ScrollViewer { Content=assetLayers, MaxHeight=190, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled } };
        Grid.SetRow(layerSection,3);pane.Children.Add(layerSection);
        var footer=new StackPanel { Spacing=4 }; footer.Children.Add(assetLoading);footer.Children.Add(assetStatus);Grid.SetRow(footer,4);pane.Children.Add(footer);
        assetPicker.SelectionChanged += async (_,_) => { if(!populatingAssets && assetPicker.SelectedItem is ComboBoxItem { Tag:string id })await Guard(()=>RefreshAssetsAsync(id)); };
        return pane;
    }
    void ResetAssetPreview()
    {
        assetLoadRevision++;selectedAsset=null;assetImage.Source=null;assetEmpty.Visibility=Visibility.Visible;assetLayers.Children.Clear();assetStatus.Text="";
        populatingAssets=true;assetPicker.Items.Clear();populatingAssets=false;assetLoading.Visibility=Visibility.Collapsed;
    }
    async Task RefreshAssetsAsync(string? requestedId=null)
    {
        if(chat==null){ResetAssetPreview();return;}
        int revision=++assetLoadRevision, chatId=chat.Id;var store=CurrentAssets();
        string? previous=requestedId??selectedAsset?.Id;
        assetLoading.Visibility=Visibility.Visible;
        try
        {
            var docs=await Task.Run(()=>store.ListAsync(CancellationToken.None));
            if(revision!=assetLoadRevision||chat?.Id!=chatId)return;
            var doc=docs.FirstOrDefault(d=>d.Id==previous)??docs.FirstOrDefault();
            populatingAssets=true;
            try
            {
                assetPicker.Items.Clear();
                foreach(var d in docs){var item=new ComboBoxItem { Content=d.Name, Tag=d.Id };assetPicker.Items.Add(item);if(d.Id==doc?.Id)assetPicker.SelectedItem=item;}
            }
            finally{populatingAssets=false;}
            selectedAsset=doc;assetLayers.Children.Clear();assetEmpty.Visibility=doc==null?Visibility.Visible:Visibility.Collapsed;
            if(doc==null){assetImage.Source=null;assetStatus.Text="";return;}
            var bytes=await Task.Run(()=>AssetRenderer.Preview(doc,1400,true));
            if(revision!=assetLoadRevision||chat?.Id!=chatId)return;
            using var stream=new MemoryStream(bytes);using var randomAccess=stream.AsRandomAccessStream();
            var bitmap=new BitmapImage();await bitmap.SetSourceAsync(randomAccess);
            if(revision!=assetLoadRevision||chat?.Id!=chatId)return;
            assetImage.Source=bitmap;
            assetStatus.Text=$"{doc.Width} × {doc.Height} px  ·  {doc.Layers.Sum(l=>l.Shapes.Count)} {WorkflowText("formes","shapes")}  ·  r{doc.Revision}";
            foreach(var layer in doc.Layers.AsEnumerable().Reverse())
            {
                var row=new Grid { ColumnSpacing=5 };row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var visible=new CheckBox { Content=$"{layer.Name} · {layer.Shapes.Count}",IsChecked=layer.Visible,MinWidth=0 };
                visible.Click+=async(_,_)=>await Guard(async()=> {
                    if(chat?.Id!=chatId)return;
                    await store.UpdateAsync(doc.Id,doc.Revision,d=>d.Layers.Single(l=>l.Id==layer.Id).Visible=visible.IsChecked==true,CancellationToken.None);
                    await RefreshAssetsAsync(doc.Id);
                });row.Children.Add(visible);
                async Task Move(int delta)
                {
                    if(chat?.Id!=chatId)return;
                    int index=doc.Layers.IndexOf(layer)+delta;if(index<0||index>=doc.Layers.Count)return;
                    await store.UpdateAsync(doc.Id,doc.Revision,d=>AssetTools.Edit(d,new JsonArray(new JsonObject { ["action"]="move_layer",["layer_id"]=layer.Id,["index"]=index })),CancellationToken.None);
                    await RefreshAssetsAsync(doc.Id);
                }
                var actions=Row(Action("↑",()=>Move(1)),Action("↓",()=>Move(-1)));Grid.SetColumn(actions,1);row.Children.Add(actions);
                assetLayers.Children.Add(row);
            }
        }
        finally {if(revision==assetLoadRevision)assetLoading.Visibility=Visibility.Collapsed;}
    }
    async Task CreateAssetManuallyAsync()
    {
        if(chat==null)return;
        var name=new TextBox { Text=WorkflowText("Nouvel asset","New asset"),Header=WorkflowText("Nom","Name") };
        var width=new NumberBox { Header=WorkflowText("Largeur","Width"),Value=512,Minimum=1,Maximum=4096 };
        var height=new NumberBox { Header=WorkflowText("Hauteur","Height"),Value=512,Minimum=1,Maximum=4096 };
        var panel=new StackPanel { Spacing=8 };panel.Children.Add(name);panel.Children.Add(width);panel.Children.Add(height);
        var dialog=new ContentDialog { XamlRoot=root.XamlRoot,Title=WorkflowText("Nouveau canevas","New canvas"),Content=panel,PrimaryButtonText=WorkflowText("Créer","Create"),CloseButtonText=WorkflowText("Annuler","Cancel") };
        var store=CurrentAssets();int chatId=chat.Id;
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var doc=await store.CreateAsync(name.Text,(int)width.Value,(int)height.Value,"none",CancellationToken.None);
        if(chat?.Id==chatId)await RefreshAssetsAsync(doc.Id);
    }
    async Task SaveAssetAsAsync()
    {
        if(selectedAsset is not {} selected)return;
        var doc=selected.Clone();string format=assetFormat.SelectedItem?.ToString()?.ToLowerInvariant()??"svg";
        bool transparent=assetTransparent.IsChecked==true;string? background=transparent?null:assetExportBackground.Text;
        float scale=assetScale.SelectedIndex switch {1=>2,2=>4,_=>1};
        var picker=new FileSavePicker { SuggestedFileName=doc.Name };
        picker.FileTypeChoices.Add(format.ToUpperInvariant(),new List<string>{"."+format});InitializePicker(picker,this);
        var file=await picker.PickSaveFileAsync();if(file==null)return;
        assetLoading.Visibility=Visibility.Visible;
        try {var bytes=await Task.Run(()=>AssetRenderer.Export(doc,format,transparent,background,scale));await FileIO.WriteBytesAsync(file,bytes);ShowStatus(WorkflowText("Asset enregistré : ","Asset saved: ")+file.Path);}
        finally{assetLoading.Visibility=Visibility.Collapsed;}
    }
}
