using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    sealed class ConversationBrowser(int id, string tabId)
    {
        public int Id { get; } = id;
        public string TabId { get; } = tabId;
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
    readonly Dictionary<(int ChatId, string TabId), ConversationBrowser> conversationBrowsers = [];
    readonly Dictionary<int, string> selectedBrowserTabs = [];
    readonly AsyncLocal<int?> browserConversation = new();
    readonly AsyncLocal<string?> browserTab = new();
    readonly TabView webTabs = new() { IsAddTabButtonVisible = true, TabWidthMode = TabViewWidthMode.SizeToContent };
    readonly Grid browserHost = new();
    readonly Grid backgroundBrowsers = new() { Width = 1024, Height = 768, Opacity = 0, IsHitTestVisible = false };
    bool populatingWebTabs;
    string SelectedBrowserTab(int id)
    {
        if (selectedBrowserTabs.TryGetValue(id, out var selected) && conversationBrowsers.ContainsKey((id, selected))) return selected;
        var first = conversationBrowsers.Keys.FirstOrDefault(key => key.ChatId == id);
        selected = first.TabId ?? "tab-1";
        selectedBrowserTabs[id] = selected;
        return selected;
    }
    ConversationBrowser BrowserFor(int chatId, string tabId)
    {
        if (!conversationBrowsers.TryGetValue((chatId, tabId), out var value))
        {
            value = new(chatId, tabId); conversationBrowsers.Add((chatId, tabId), value);
            backgroundBrowsers.Children.Add(value.View);
            value.View.PointerMoved += (_, e) => { var p = e.GetCurrentPoint(value.View).Position; value.PointerX = p.X; value.PointerY = p.Y; };
            SyncBrowserPresentation();
        }
        return value;
    }
    ConversationBrowser CurrentBrowser
    {
        get
        {
            var id = browserConversation.Value ?? chat?.Id ?? 0;
            return BrowserFor(id, browserTab.Value ?? SelectedBrowserTab(id));
        }
    }
    Microsoft.UI.Xaml.Controls.WebView2 browser => CurrentBrowser.View;
    bool browserReady { get => CurrentBrowser.Ready; set => CurrentBrowser.Ready = value; }
    string? previewFolder { get => CurrentBrowser.PreviewFolder; set => CurrentBrowser.PreviewFolder = value; }
    string? previewHost { get => CurrentBrowser.PreviewHost; set => CurrentBrowser.PreviewHost = value; }
    double? browserPointerX { get => CurrentBrowser.PointerX; set => CurrentBrowser.PointerX = value; }
    double? browserPointerY { get => CurrentBrowser.PointerY; set => CurrentBrowser.PointerY = value; }
    IDisposable BrowserScope(int? id = null, string? tabId = null)
    {
        var previousConversation = browserConversation.Value;
        var previousTab = browserTab.Value;
        browserConversation.Value = id ?? previousConversation ?? chat?.Id ?? 0;
        browserTab.Value = tabId ?? (id.HasValue ? null : previousTab);
        return new BrowserScopeLease(() => { browserConversation.Value = previousConversation; browserTab.Value = previousTab; });
    }
    sealed class BrowserScopeLease(Action restore) : IDisposable { public void Dispose() => restore(); }
    ConversationBrowser NewBrowserTab(int id, bool select = true)
    {
        if (conversationBrowsers.Keys.Count(key => key.ChatId == id) >= 12) throw new InvalidOperationException("Maximum 12 onglets Web par conversation.");
        var tabId = conversationBrowsers.Keys.Any(key => key.ChatId == id) ? Guid.NewGuid().ToString("N")[..8] : "tab-1";
        var item = BrowserFor(id, tabId);
        if (select) selectedBrowserTabs[id] = tabId;
        SyncBrowserPresentation();
        return item;
    }
    void SelectBrowserTab(int id, string tabId)
    {
        if (!conversationBrowsers.ContainsKey((id, tabId))) throw new ArgumentException("Onglet Web introuvable / Browser tab not found.");
        selectedBrowserTabs[id] = tabId;
        SyncBrowserPresentation();
    }
    string BrowserTabTitle(ConversationBrowser item)
    {
        if (item.Address == "about:blank") return WorkflowText("Nouvel onglet", "New tab");
        if (Path.IsPathFullyQualified(item.Address) && !item.Address.Contains("://")) return Path.GetFileName(item.Address);
        return Uri.TryCreate(item.Address, UriKind.Absolute, out var uri) ? uri.Host : item.Address;
    }
    void SyncWebTabs()
    {
        if (populatingWebTabs) return;
        populatingWebTabs = true;
        try
        {
            var id = chat?.Id ?? 0;
            webTabs.TabItems.Clear();
            foreach (var item in conversationBrowsers.Values.Where(item => item.Id == id))
            {
                var title = BrowserTabTitle(item);
                webTabs.TabItems.Add(new TabViewItem { Tag = item.TabId, Header = title.Length > 25 ? title[..24] + "…" : title });
            }
            webTabs.SelectedItem = webTabs.TabItems.OfType<TabViewItem>().FirstOrDefault(item => (string)item.Tag == SelectedBrowserTab(id));
        }
        finally { populatingWebTabs = false; }
    }
    void SyncBrowserPresentation()
    {
        var shownId = chat?.Id ?? 0;
        var selected = SelectedBrowserTab(shownId);
        foreach (var item in conversationBrowsers.Values)
        {
            var destination = item.Id == shownId && item.TabId == selected && item.Ready && browserVisible && toolTabs.SelectedIndex == 0 ? browserHost : backgroundBrowsers;
            if (item.View.Parent == destination) continue;
            (item.View.Parent as Panel)?.Children.Remove(item.View);
            destination.Children.Add(item.View);
        }
        address.Text = conversationBrowsers.TryGetValue((shownId, selected), out var current) ? current.Address : "about:blank";
        if (current?.Ready == true) browserHost.Children.Remove(browserNotice);
        else if (browserVisible && toolTabs.SelectedIndex == 0) ShowBrowserNotice();
        SyncWebTabs();
    }
    void DisposeBrowser(ConversationBrowser item)
    {
        item.Closed = true; item.Ready = false;
        try { (item.View.Parent as Panel)?.Children.Remove(item.View);
#if WINDOWS
            item.View.Close();
#else
            item.PreviewServer?.Dispose();
#endif
        }
        catch (System.Runtime.InteropServices.COMException) { }
    }
    void CloseBrowserTab(int id, string tabId, bool replaceIfLast = true)
    {
        if (!conversationBrowsers.Remove((id, tabId), out var item)) return;
        DisposeBrowser(item);
        if (selectedBrowserTabs.GetValueOrDefault(id) == tabId) selectedBrowserTabs.Remove(id);
        if (replaceIfLast && !conversationBrowsers.Keys.Any(key => key.ChatId == id)) NewBrowserTab(id);
        SyncBrowserPresentation();
    }
    void CloseConversationBrowser(int id)
    {
        foreach (var key in conversationBrowsers.Keys.Where(key => key.ChatId == id).ToArray())
        {
            var item = conversationBrowsers[key]; conversationBrowsers.Remove(key); DisposeBrowser(item);
        }
        selectedBrowserTabs.Remove(id);
        SyncBrowserPresentation();
    }
}
