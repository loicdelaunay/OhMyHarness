using OhMyHarness.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

using static OhMyHarness.App.UiText;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async Task ExportConversationAsync()
    {
        if (chat == null) return;
        var id = chat.Id;
        var activeRun = ActiveRun;
        await using var exportDb = new HarnessDb();
        var document = await ConversationExport.CreateAsync(exportDb, id, provider?.Id ?? state.ProviderId,
            activeRun != null, browserAccess.IsOn, browserDomAccess.IsOn, activeRun?.ExportProgress);
        if (document.UseClipboard)
        {
            try
            {
                var data = new DataPackage(); data.SetText(document.Markdown);
                Clipboard.SetContent(data); Clipboard.Flush();
                status.Text = T("Conversation copiée en Markdown.");
                return;
            }
            catch (Exception) { /* Clipboard busy/unavailable: offer the same lossless file export. */ }
        }
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(document.FileName) };
        picker.FileTypeChoices.Add("Markdown", new List<string> { ".md" });
        InitializePicker(picker, this);
        var file = await picker.PickSaveFileAsync();
        if (file == null) { status.Text = T("Export annulé."); return; }
        await FileIO.WriteTextAsync(file, document.Markdown);
        status.Text = T("Conversation enregistrée : ") + file.Path;
    }
}
