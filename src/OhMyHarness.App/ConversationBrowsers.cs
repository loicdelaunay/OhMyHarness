using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    sealed class ConversationBrowser(int id)
    {
        public int Id { get; } = id;
        public Microsoft.UI.Xaml.Controls.WebView2 View { get; } = new();
#if !WINDOWS
        public LocalPreviewServer? PreviewServer;
#endif
        public Task? Initialization;
        public bool Ready;
        public bool Closed;
        public string? PreviewFolder, PreviewHost;
        public double? PointerX, PointerY;
        public string Address = "about:blank";
    }
    readonly Dictionary<int, ConversationBrowser> conversationBrowsers = [];
    readonly AsyncLocal<int?> browserConversation = new();
    readonly Grid browserHost = new();
    readonly Grid backgroundBrowsers = new() { Width = 1024, Height = 768, Opacity = 0, IsHitTestVisible = false };
    ConversationBrowser CurrentBrowser
    {
        get
        {
            var id = browserConversation.Value ?? chat?.Id ?? 0;
            if (!conversationBrowsers.TryGetValue(id, out var value))
            {
                value = new(id); conversationBrowsers.Add(id, value);
                backgroundBrowsers.Children.Add(value.View);
                value.View.PointerMoved += (_, e) => { var p = e.GetCurrentPoint(value.View).Position; value.PointerX = p.X; value.PointerY = p.Y; };
                SyncBrowserPresentation();
            }
            return value;
        }
    }
    Microsoft.UI.Xaml.Controls.WebView2 browser => CurrentBrowser.View;
    bool browserReady { get => CurrentBrowser.Ready; set => CurrentBrowser.Ready = value; }
    string? previewFolder { get => CurrentBrowser.PreviewFolder; set => CurrentBrowser.PreviewFolder = value; }
    string? previewHost { get => CurrentBrowser.PreviewHost; set => CurrentBrowser.PreviewHost = value; }
    double? browserPointerX { get => CurrentBrowser.PointerX; set => CurrentBrowser.PointerX = value; }
    double? browserPointerY { get => CurrentBrowser.PointerY; set => CurrentBrowser.PointerY = value; }
    IDisposable BrowserScope(int? id = null)
    {
        var previous = browserConversation.Value;
        browserConversation.Value = id ?? previous ?? chat?.Id ?? 0;
        return new BrowserScopeLease(() => browserConversation.Value = previous);
    }
    sealed class BrowserScopeLease(Action restore) : IDisposable { public void Dispose() => restore(); }
    void SyncBrowserPresentation()
    {
        foreach (var item in conversationBrowsers.Values)
        {
            var destination = item.Id == (chat?.Id ?? 0) && browserVisible && toolTabs.SelectedIndex == 0 ? browserHost : backgroundBrowsers;
            if (item.View.Parent == destination) continue;
            (item.View.Parent as Panel)?.Children.Remove(item.View);
            destination.Children.Add(item.View);
        }
        address.Text = chat != null && conversationBrowsers.TryGetValue(chat.Id, out var selected) ? selected.Address : "about:blank";
    }
    void CloseConversationBrowser(int id)
    {
        if (!conversationBrowsers.Remove(id, out var item)) return;
        item.Closed = true; item.Ready = false;
        try { (item.View.Parent as Panel)?.Children.Remove(item.View);
#if WINDOWS
            item.View.Close();
#else
            item.PreviewServer?.Dispose();
#endif
 } catch (System.Runtime.InteropServices.COMException) { }
    }
}
