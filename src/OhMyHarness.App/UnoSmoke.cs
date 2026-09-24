using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media.Imaging;
using OhMyHarness.Core;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Windows.Storage.Streams;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async Task UnoSmokeAsync(string output)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("OHMYHARNESS_THEME_SMOKE") == "1") { await SmokeThemes(output); return; }
            if (Environment.GetEnvironmentVariable("OHMYHARNESS_UX_SMOKE") == "1") { await SmokeConversationEnhancements(output); return; }
            static IEnumerable<FrameworkElement> Descendants(DependencyObject? parent)
            {
                if(parent==null)yield break;
                if(parent is FrameworkElement element)yield return element;
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
                    foreach(var child in Descendants(VisualTreeHelper.GetChild(parent,i)))yield return child;
            }
            static void Click(Button button) => (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider
                ?? throw new InvalidOperationException("Button invocation unavailable")).Invoke();
            var fixture=new Provider { Name="Uno fixture",Model="model-a",DetectedModelsJson="[\"model-a\",\"model-b\"]",SelectedModelsJson="[\"model-a\",\"model-b\"]" };
            db.Providers.Add(fixture);await db.SaveChangesAsync();provider=fixture;PopulateModelSelector();
            var browserProject=new Project { Name="Browser smoke", Chats=[new Chat { Title="Embedded browser" }] };
            db.Projects.Add(browserProject);await db.SaveChangesAsync();
            projects.ItemsSource=new[] { browserProject };projects.SelectedItem=browserProject;await SelectProject();
            var originalChat = chat ?? throw new Exception("Conversation fixture was not selected.");
            var initialToolVisible = browserVisible;
            var initialToolTab = toolTabs.SelectedIndex;
            var focusFeatures = state.FeaturesJson;
            var autoFocus = FeatureSettings.Read(focusFeatures); autoFocus.AutoFocusTool = true; state.FeaturesJson = autoFocus.Json();
            var firstTerminal = terminals.Create(originalChat.Id, false, "Alpha", output);
            var secondTerminal = terminals.Create(originalChat.Id, false, "Beta", output);
            using (var focusRun = new ConversationRun(originalChat, browserProject, fixture, state, "", []) { Messages = CreateMessagePanel() })
            {
                await FocusLatestToolAsync(focusRun, "read_terminal", new JsonObject { ["terminal_id"] = secondTerminal.Id });
                if (!browserVisible || toolTabs.SelectedIndex != 1 ||
                    terminalTabs.SelectedItem is not TabViewItem { Tag: string focusedId } || focusedId != secondTerminal.Id)
                    throw new Exception("Auto-focus opened Terminal without selecting the requested terminal tab.");
                await FocusLatestToolAsync(focusRun, "start_terminal", new JsonObject { ["terminal_id"] = firstTerminal.Id });
                if (terminalTabs.SelectedItem is not TabViewItem { Tag: string nextId } || nextId != firstTerminal.Id)
                    throw new Exception("Auto-focus did not follow the next terminal tab.");
            }
            await terminals.RemoveChatAsync(originalChat.Id); RefreshTerminals(); state.FeaturesJson = focusFeatures;
            browserVisible = initialToolVisible; browserPanel.Visibility = initialToolVisible ? Visibility.Visible : Visibility.Collapsed;
            toolTabs.SelectedIndex = initialToolTab; SyncBrowserPresentation(); ResizeLayout();
            db.Chats.Add(new Chat { ProjectId = browserProject.Id, Title = "Second chat" });
            await db.SaveChangesAsync();
            await SelectProject();
            chatSearch.Text = "second";
            await Task.Delay(100);
            if (chats.Items.Count != 1 || chats.Items[0] is not Chat { Title: "Second chat" } || chat?.Id != originalChat.Id)
                throw new Exception("Conversation search changed the active chat or failed to filter the list.");
            await Capture(root, Path.Combine(output, "conversation-search.png"));
            chatSearch.Text = "no matching conversation";
            await Task.Delay(100);
            if (chats.Items.Count != 0 || chat?.Id != originalChat.Id)
                throw new Exception($"An empty conversation search changed the active chat: results={chats.Items.Count}, active={chat?.Id}, original={originalChat.Id}.");
            chatSearch.Text = string.Empty;
            await Task.Delay(500);
            if (chats.Items.Count != 2 || (chats.SelectedItem as Chat)?.Id != originalChat.Id)
                throw new Exception("Clearing conversation search did not restore the selection.");
            if (allProjectChats.Any(item => chats.ContainerFromItem(item) is not ListViewItem))
                throw new Exception("Conversation search did not restore all visible rows.");
            var secondChat = allProjectChats.Single(item => item.Title == "Second chat");
            chats.SelectedItem = secondChat;
            for (var attempt = 0; attempt < 20 && chat?.Id != secondChat.Id; attempt++) await Task.Delay(50);
            if (chat?.Id != secondChat.Id) throw new Exception("Selecting a search result did not open the conversation.");
            await Task.Delay(150);
            chats.SelectedItem = originalChat;
            for (var attempt = 0; attempt < 20 && chat?.Id != originalChat.Id; attempt++) await Task.Delay(50);
            if (chat?.Id != originalChat.Id) throw new Exception("Conversation selection did not restore the original chat.");
            await Task.Delay(150);
            var originalLanguage = UiText.Language;
            await SmokeConversationLoadingAsync(originalChat, secondChat, output);
            UiText.Language = "en";
            ApplyLanguage();
            if (chatSearch.PlaceholderText != "Search conversations…" || Descendants(chats).OfType<TextBlock>()
                    .Any(label => Equals(label.Tag, "conversation-title") && label.Text == "conversation-title"))
                throw new Exception("Conversation search or titles did not survive language switching.");
            UiText.Language = originalLanguage;
            ApplyLanguage();
            await Capture(root, Path.Combine(output, "conversation-list.png"));
            var hoverContainer = chats.ContainerFromItem(secondChat) as ListViewItem
                ?? throw new Exception("Conversation hover target was not realized.");
            hoveredConversationContainer = hoverContainer;
            RefreshConversationCard(hoverContainer);
            var hoverCard = Descendants(hoverContainer).OfType<Border>().Single(element => Equals(element.Tag, "conversation-card"));
            if (!ReferenceEquals(hoverCard.Background, FluentDesign.Resource("ConversationHoverFillBrush")))
                throw new Exception("Conversation hover did not change the card background.");
            await Capture(root, Path.Combine(output, "conversation-hover.png"));
            hoveredConversationContainer = null;
            RefreshConversationCard(hoverContainer);
            var messageHost = scroll.Content as StackPanel ?? throw new Exception("Visible chat message panel is missing.");
            var previousMessages = messageHost.Children.ToArray();
            messageHost.Children.Clear();
            AddMessage("user", "Ouvre le navigateur et recherche un article sur Lamborghini.", target: messageHost);
            var demoResponse = AddAssistantMessage("[Réponse interrompue]", target: messageHost);
            var demoCard = demoResponse.Container ?? throw new Exception("Assistant message card was not created.");
            if (demoCard.Tag is not MessageActionMenu demoActions || demoActions.Menu.Items.OfType<MenuFlyoutItem>().Count(item => item.Text == UiText.T("Copier")) != 1
                || Descendants(demoCard).OfType<Button>().Any(button => button.Content?.ToString() == UiText.T("Copier")))
                throw new Exception("Copy is not exclusively in the message actions menu.");
            AddHistoryActions(new Message { ChatId = originalChat.Id, Role = "assistant", State = "interrupted" }, demoCard);
            if (demoActions.Menu.Items.Count != 1) throw new Exception("Interrupted assistant messages lost their copy menu.");
            AddHistoryActions(new Message { ChatId = originalChat.Id, Role = "assistant", State = "complete" }, demoCard);
            if (demoActions.Menu.Items.Count != 3) throw new Exception("Completed assistant messages lost fork/resume actions.");
            AddToolMessage("mcp_2147483647_chrome_list_pages_with_a_long_tool_identifier", "{}", "Un onglet disponible : about:blank", target: messageHost);
            demoActions.Button.Visibility = Visibility.Visible;
            scroll.ChangeView(null, 0, null);
            await Task.Delay(100);
            await Capture(root, Path.Combine(output, "message-actions.png"));
            var bubbleTheme = FeatureSettings.Read(state.FeaturesJson).Theme;
            ApplyTheme("fluent-light");
            await Task.Delay(100);
            await Capture(root, Path.Combine(output, "message-bubbles-light.png"));
            ApplyTheme(bubbleTheme);
            messageHost.Children.Clear();
            foreach (var previousMessage in previousMessages) messageHost.Children.Add(previousMessage);
            await ShowToolAsync(0);
            browser.CoreWebView2.Navigate("about:blank");
            await Task.Delay(1200);
            await browser.ExecuteScriptAsync("document.body.innerHTML = '<h1 id=\"embedded-browser-smoke\">Navigateur intégré</h1>'; document.body.style.background = '#f5e749'");
            var webText=await browser.ExecuteScriptAsync("document.querySelector('#embedded-browser-smoke')?.textContent");
            if(!webText.Contains("Navigateur intégré",StringComparison.Ordinal))throw new Exception("Embedded browser DOM unavailable: "+webText);
#if !WINDOWS
            var evaluated=JsonNode.Parse(await BrowserDevToolsAsync("Runtime.evaluate","{\"expression\":\"1+1\"}"));
            if(evaluated?["result"]?["value"]?.GetValue<int>()!=2)throw new Exception("Embedded browser JavaScript evaluation failed.");
            await browser.ExecuteScriptAsync("document.body.innerHTML = '<button id=\"browser-click-smoke\">Cliquer</button>'; window.smokeClicks=0; document.querySelector('#browser-click-smoke').onclick=()=>window.smokeClicks++");
            var pointJson=await browser.ExecuteScriptAsync("JSON.stringify((()=>{const r=document.querySelector('#browser-click-smoke').getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2}})())");
            var point=JsonNode.Parse(System.Text.Json.JsonSerializer.Deserialize<string>(pointJson)!)!;
            var clickX=point["x"]!.GetValue<double>();var clickY=point["y"]!.GetValue<double>();
            await BrowserDevToolsAsync("Input.dispatchMouseEvent",System.Text.Json.JsonSerializer.Serialize(new { type="mousePressed",x=clickX,y=clickY,button="left",clickCount=1 }));
            await BrowserDevToolsAsync("Input.dispatchMouseEvent",System.Text.Json.JsonSerializer.Serialize(new { type="mouseReleased",x=clickX,y=clickY,button="left",clickCount=1 }));
            if(await browser.ExecuteScriptAsync("window.smokeClicks")!="1")throw new Exception("Embedded browser click did not reach the page.");
            await browser.ExecuteScriptAsync("window.smokeContext=0; document.querySelector('#browser-click-smoke').oncontextmenu=e=>{e.preventDefault();window.smokeContext++}");
            await BrowserDevToolsAsync("Input.dispatchMouseEvent",System.Text.Json.JsonSerializer.Serialize(new { type="mouseReleased",x=clickX,y=clickY,button="right",clickCount=1 }));
            if(await browser.ExecuteScriptAsync("window.smokeContext")!="1")throw new Exception("Embedded browser right-click did not reach the page.");
            using(var pageServer=new TcpListener(IPAddress.Loopback,0))
            {
                pageServer.Start();
                var port=((IPEndPoint)pageServer.LocalEndpoint).Port;
                var serve=Task.Run(async()=>
                {
                    using var client=await pageServer.AcceptTcpClientAsync();using var stream=client.GetStream();
                    using var headers=new MemoryStream();var request=new byte[1];
                    while(!Encoding.ASCII.GetString(headers.ToArray()).EndsWith("\r\n\r\n",StringComparison.Ordinal))
                    {
                        if(headers.Length>16384)throw new IOException("Invalid browser smoke request.");
                        await stream.ReadExactlyAsync(request);headers.WriteByte(request[0]);
                    }
                    var body=Encoding.UTF8.GetBytes("<html><body>Page locale intégrée</body></html>");
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
                    await stream.WriteAsync(body);
                });
                var page=await NavigateCoreAsync(new Uri($"http://127.0.0.1:{port}/"),CancellationToken.None);
                await serve;
                if(!page.Contains("Page locale intégrée",StringComparison.Ordinal))throw new Exception("Embedded browser navigation did not return page content.");
            }
            browser.CoreWebView2.Navigate("about:blank");await Task.Delay(250);
            await browser.ExecuteScriptAsync("document.body.innerHTML = '<h1 id=\"embedded-browser-smoke\">Navigateur intégré</h1>'; document.body.style.background = '#f5e749'");
            toolTabs.SelectedIndex=1;await Task.Delay(150);
            if(OperatingSystem.IsWindows())
            {
                var own=DesktopApplications.List().Where(w=>w.ProcessId==Environment.ProcessId && w.Visible && !w.Minimized)
                    .OrderBy(w=>Math.Abs(w.Width-root.ActualWidth)+Math.Abs(w.Height-root.ActualHeight)).First();
                var snapshot=await CaptureApplicationPixelsAsync(own,DesktopInterop.GetCursorSnapshot(),CancellationToken.None);
                var yellow=0;
                for(var pixel=0;pixel<snapshot.Pixels.Length;pixel+=4)
                    if(Math.Abs(snapshot.Pixels[pixel]-0x49)<8 && Math.Abs(snapshot.Pixels[pixel+1]-0xE7)<8 && Math.Abs(snapshot.Pixels[pixel+2]-0xF5)<8)yellow++;
                if(yellow>snapshot.Width*snapshot.Height/100)throw new Exception("Embedded browser remained visible over the Terminal tab.");
            }
            toolTabs.SelectedIndex=0;await Task.Delay(250);
            if(await browser.ExecuteScriptAsync("document.querySelector('#embedded-browser-smoke')?.textContent")!="\"Navigateur intégré\"")
                throw new Exception("Embedded browser lost its page after switching tool tabs.");
#endif
#if !WINDOWS
            if (OperatingSystem.IsWindows())
            {
                var browserCapture=await CaptureEmbeddedBrowserDesktopAsync((int)browser.ActualWidth,(int)browser.ActualHeight,CancellationToken.None);
                await File.WriteAllBytesAsync(Path.Combine(output,"embedded-browser-crop.png"),browserCapture);
            }
#endif
            if(modelSelector.Items.OfType<ModelChoice>().Count(x=>x.ProviderId==fixture.Id)!=2)throw new Exception("Model picker did not refresh.");
            var bubble=new StackPanel();MarkdownRenderer.RenderTo(bubble,"# Uno Platform\n\n**Texte sélectionnable** avec [un lien](https://example.com).\n\n```csharp\nvar task = new ScheduledTask();\n```\n\n- [x] Modèles\n- [ ] Tâches");messages.Children.Add(bubble);
            await Task.Delay(500);
            await Capture(root,Path.Combine(output,"chat.png"));
            var menuPreview=new Flyout();BuildComposerMenu(menuPreview);
            var menuOverlay=new Border { Child=menuPreview.Content,Background=FluentDesign.Card,BorderBrush=FluentDesign.Stroke,
                BorderThickness=new(1),CornerRadius=new(12),Padding=new(10),Margin=new(20),
                HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom };
            root.Children.Add(menuOverlay);await Task.Delay(250);
            await Capture(root,Path.Combine(output,"composer-menu.png"));root.Children.Remove(menuOverlay);
            ShowStatus("Configuration enregistrée.");
            await Task.Delay(550);await Capture(root,Path.Combine(output,"status-notice-pulse.png"));
            await Task.Delay(1100);await Capture(root,Path.Combine(output,"status-notice-after-pulse.png"));
            await Task.Delay(3200);
            if(status.Text.Length!=0)throw new Exception("Temporary status did not disappear.");
            if(chat is { } selectedChat)
            {
                var expiry=DateTimeOffset.UtcNow.AddMilliseconds(350);
                conversationStatuses[selectedChat.Id]=new("Réponse terminée.",StatusKind.Notice,expiry);
                ShowStatus("Réponse terminée.",StatusKind.Notice,selectedChat.Id,expiry);
                await Task.Delay(550);
                if(status.Text.Length!=0 || conversationStatuses.ContainsKey(selectedChat.Id))
                    throw new Exception("Expired conversation status returned after completion.");
            }
            ShowStatus("Outil : desktop_screenshot",StatusKind.Activity);
            await Task.Delay(650);await Capture(root,Path.Combine(output,"status-activity.png"));
            var firstGlow=statusGlowLayer?.Opacity ?? 0;
            await Task.Delay(500);
            if(Math.Abs((statusGlowLayer?.Opacity ?? 0)-firstGlow)<.1)throw new Exception("Active glow did not animate.");
            await Task.Delay(1600);
            if(status.Text!="Outil : desktop_screenshot")throw new Exception("Active status disappeared while work was running.");
            ClearStatus();
            await SetFontZoomAsync(150);await Task.Delay(350);
            await Capture(root,Path.Combine(output,"chat-zoom-150.png"));
            await SetFontZoomAsync(100);
            var originalFeatures=state.FeaturesJson;
            var branded=FeatureSettings.Read(originalFeatures);branded.ApplicationName="Fly workspace";
            branded.LogoPath=await BrandingAssets.SaveLogoAsync(DefaultLogo,ReadLogo(DefaultLogo));
            branded.Theme="fly-dark";state.FeaturesJson=branded.Json();ApplyAppearance();await Task.Delay(350);
            await Capture(root,Path.Combine(output,"fly-dark.png"));
            branded.Theme="fly-light";state.FeaturesJson=branded.Json();ApplyAppearance();await Task.Delay(350);
            await Capture(root,Path.Combine(output,"fly-light.png"));
            var brandingEditor=BuildBrandingSettings();((Expander)brandingEditor.Panel.Children[0]).IsExpanded=true;
            var brandingOverlay=new ScrollViewer { Content=brandingEditor.Panel,Background=FluentDesign.Card,Padding=new(24) };root.Children.Add(brandingOverlay);
            await Task.Delay(350);await Capture(root,Path.Combine(output,"branding-settings.png"));root.Children.Remove(brandingOverlay);
            state.FeaturesJson=originalFeatures;ApplyAppearance();
            var storedTheme = FeatureSettings.Read(state.FeaturesJson).Theme;
            var previewId = AppearanceThemes.Get(storedTheme).Dark ? "fly-light" : "fly-dark";
            var cancelledSettings = EditSettingsAsync();
            settingsWindow!.Close();
            await cancelledSettings;
            if (settingsWindow != null) throw new Exception("Closing settings during loading left an orphan window.");
            var settingsRun = EditSettingsAsync();
            if (settingsWindow?.Content is not Border) throw new Exception("Settings did not immediately show its loading placeholder.");
            for (var attempt = 0; attempt < 100 && !Descendants(settingsWindow?.Content).OfType<ComboBox>()
                    .Any(box => box.Items.Count == AppearanceThemes.All.Count && box.Items.OfType<string>().Contains("Fly dark")); attempt++) await Task.Delay(50);
            if (settingsWindow?.Content is not FrameworkElement settingsRoot) throw new Exception("Settings window did not open for theme preview.");
            await SmokeMemorySettingsAsync(settingsRoot, output);
            var previewPicker = Descendants(settingsRoot).OfType<ComboBox>()
                .FirstOrDefault(box => box.Items.Count == AppearanceThemes.All.Count && box.Items.OfType<string>().Contains("Fly dark"));
            if (previewPicker == null) throw new Exception("Theme picker is missing from General settings.");
            previewPicker.SelectedIndex = AppearanceThemes.All.ToList().FindIndex(item => item.Id == previewId);
            await Task.Delay(150);
            var previewElementTheme = AppearanceThemes.Get(previewId).Dark ? ElementTheme.Dark : ElementTheme.Light;
            if (root.RequestedTheme != previewElementTheme || settingsRoot.RequestedTheme != previewElementTheme || FeatureSettings.Read(state.FeaturesJson).Theme != storedTheme)
                throw new Exception("Theme preview did not update both windows without saving.");
            await Capture(settingsRoot, Path.Combine(output, "theme-preview.png"));
            Click(Descendants(settingsRoot).OfType<Button>().Single(button => button.Content?.ToString() == UiText.T("Annuler")));
            await settingsRun;
            if (root.RequestedTheme != (AppearanceThemes.Get(storedTheme).Dark ? ElementTheme.Dark : ElementTheme.Light)
                || FeatureSettings.Read(state.FeaturesJson).Theme != storedTheme)
                throw new Exception("Cancel did not restore the saved theme.");
            await ShowScheduledTasks();await Task.Delay(500);
            var add=Descendants(tasksWindow!.Content!).OfType<Button>().Single(x=>x.Content?.ToString()?.Contains("Nouvelle tâche")==true);
            Click(add);await Task.Delay(400);
            var boxes=Descendants(tasksWindow.Content).OfType<TextBox>().ToList();
            boxes.Single(x=>x.Header?.ToString()=="Nom").Text="Tâche de test Uno";
            boxes.Single(x=>x.Header?.ToString()=="Instruction").Text="Résumer les sources.";
            await Capture((FrameworkElement)tasksWindow.Content!,Path.Combine(output,"task-editor.png"));
            var save=Descendants(tasksWindow.Content).OfType<Button>().Single(x=>x.Content?.ToString()=="Enregistrer / Save");
            Click(save);await Task.Delay(700);
            using(var check=new HarnessDb())if(!check.ScheduledTasks.Any(x=>x.Name=="Tâche de test Uno"))throw new Exception("Task editor did not save.");
            tasksWindow.Close();await Task.Delay(100);
            await SmokeScheduledExecutionAsync();
            var editor=BuildProviderEditor(fixture.Id);root.Children.Add(new ScrollViewer { Content=editor.Panel,Background=FluentDesign.Card,Padding=new(24),HorizontalAlignment=HorizontalAlignment.Stretch });
            await Task.Delay(300);await Capture(root,Path.Combine(output,"provider-cards.png"));
            var providerCards=(StackPanel)editor.Panel.Children[1];
            var fixtureCard=providerCards.Children.OfType<Border>().Single(card=>Descendants(card).OfType<TextBlock>().Any(text=>text.Text=="Uno fixture"));
            Descendants(fixtureCard).OfType<Expander>().Single().IsExpanded=true;
            await Task.Delay(150);
            var modelSearch=Descendants(fixtureCard).OfType<TextBox>().Single(x=>x.PlaceholderText?.Contains("modèle",StringComparison.OrdinalIgnoreCase)==true);
            modelSearch.Text="model-b";
            await Task.Delay(100);
            var visibleModels=Descendants(fixtureCard).OfType<CheckBox>().Where(x=>x.Content?.ToString()?.StartsWith("model-")==true).Select(x=>x.Content?.ToString()).ToArray();
            if(visibleModels.Length!=1 || visibleModels[0]!="model-b")
                throw new Exception("Provider model search did not filter the visible page: "+string.Join(",",visibleModels));
            Click(Descendants(fixtureCard).OfType<Button>().Single(x=>x.Content?.ToString()=="Tout décocher"));
            Click(Descendants(fixtureCard).OfType<Button>().Single(x=>x.Content?.ToString()=="Tout cocher"));
            Descendants(fixtureCard).OfType<CheckBox>().Single(x=>x.Content?.ToString()=="model-b").IsChecked=false;
            editor.Commit();await SaveProviderDraftsAsync(editor);PopulateModelSelector();
            if(modelSelector.Items.OfType<ModelChoice>().Where(x=>x.ProviderId==fixture.Id).Select(x=>x.Model).Single()!="model-a")
                throw new Exception("Provider card checkbox did not update the model picker.");
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            using(var server=new TcpListener(IPAddress.Loopback,0))
            {
                server.Start();
                fixture.BaseUrl=$"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/v1";
                await db.SaveChangesAsync();
                var testedEditor=BuildProviderEditor(fixture.Id);
                root.Children.Add(new ScrollViewer { Content=testedEditor.Panel,Background=FluentDesign.Card,Padding=new(24) });
                var gear=Descendants(testedEditor.Panel).OfType<Button>().Single(x=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(x).Contains("Uno fixture"));
                Click(gear);
                var test=Descendants(testedEditor.Panel).OfType<Button>().Single(x=>x.Content?.ToString()=="Tester la connexion");
                var serve=Task.Run(async()=>
                {
                    using var client=await server.AcceptTcpClientAsync(timeout.Token);
                    using var stream=client.GetStream();
                    using var header=new MemoryStream();var one=new byte[1];
                    while(!Encoding.ASCII.GetString(header.ToArray()).EndsWith("\r\n\r\n"))
                    {
                        if(await stream.ReadAsync(one,timeout.Token)==0 || header.Length>16384)throw new IOException("Invalid model-list request.");
                        header.WriteByte(one[0]);
                    }
                    var body=Encoding.UTF8.GetBytes("""{"data":[{"id":"model-a"},{"id":"model-b"},{"id":"model-c"}]}""");
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"),timeout.Token);
                    await stream.WriteAsync(body,timeout.Token);
                },timeout.Token);
                Click(test);await serve;
                for(var attempt=0;attempt<100 && (!test.IsEnabled || ProviderModels.Visible(fixture).Count!=3);attempt++)await Task.Delay(50);
                using var persisted=new HarnessDb();
                var saved=await persisted.Providers.SingleAsync(x=>x.Id==fixture.Id);
                if(!test.IsEnabled || ProviderModels.Visible(saved).Count!=3 || modelSelector.Items.OfType<ModelChoice>().Count(x=>x.ProviderId==fixture.Id)!=3)
                    throw new Exception("Connection test did not select, save and expose all discovered models.");
            }
            var largeModels=Enumerable.Range(0,5000).Select(index=>$"opencode/model-{index:00000}").ToArray();
            var largeProvider=new Provider { Name="Large OpenCode fixture",Kind="opencode",Model=largeModels[0],
                DetectedModelsJson=System.Text.Json.JsonSerializer.Serialize(largeModels),
                SelectedModelsJson=System.Text.Json.JsonSerializer.Serialize(new[]{largeModels[0]}) };
            db.Providers.Add(largeProvider);await db.SaveChangesAsync();
            var largeEditor=BuildProviderEditor(largeProvider.Id);
            var largeOverlay=new ScrollViewer { Content=largeEditor.Panel,Background=FluentDesign.Card,Padding=new(24) };
            root.Children.Add(largeOverlay);await Task.Delay(100);
            var largeCards=(StackPanel)largeEditor.Panel.Children[1];
            var largeCard=largeCards.Children.OfType<Border>().Single(card=>Descendants(card).OfType<TextBlock>().Any(text=>text.Text==largeProvider.Name));
            if(Descendants(largeCard).OfType<CheckBox>().Any())throw new Exception("Collapsed provider rendered model checkboxes eagerly.");
            Descendants(largeCard).OfType<Expander>().Single().IsExpanded=true;await Task.Delay(100);
            if(Descendants(largeCard).OfType<CheckBox>().Count()>50)throw new Exception("Provider rendered more than one model page.");
            var largeSearch=Descendants(largeCard).OfType<TextBox>().Single(x=>x.PlaceholderText?.Contains("modèle",StringComparison.OrdinalIgnoreCase)==true);
            largeSearch.Text="model-04999";await Task.Delay(100);
            var largeVisible=Descendants(largeCard).OfType<CheckBox>().Select(x=>x.Content?.ToString()).ToArray();
            if(largeVisible.Length!=1 || largeVisible[0]!=largeModels[^1])throw new Exception("Large provider search did not find a model outside the first page.");
            root.Children.Remove(largeOverlay);
            File.WriteAllText(Path.Combine(output,"smoke-ok.txt"),"Uno UI: async history pagination/lazy images/UI heartbeat, rapid conversation/project switching/drafts/active runs, settings loading/cancel, conversation search/selection/hover/localization, assistant message copy menu and branch actions, live theme preview/cancel, embedded browser/navigation/input/screenshot/tab switch, Markdown, model picker, task editor/save, scheduled generation/tool/history, provider cards, connection test and status lifetime passed.");
        }
        catch(Exception ex) { File.WriteAllText(Path.Combine(output,"smoke-error.txt"),ex.ToString()); }
        finally { Close(); Application.Current.Exit(); }
    }
    async Task SmokeScheduledExecutionAsync()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var server=new TcpListener(IPAddress.Loopback,0);server.Start();
        var endpoint=$"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/v1";
        var requests=new List<JsonObject>();
        var serve=Task.Run(async () =>
        {
            for(var round=0;round<3;round++)
            {
                using var client=await server.AcceptTcpClientAsync(timeout.Token);using var stream=client.GetStream();
                using var header=new MemoryStream();var one=new byte[1];
                while(!Encoding.ASCII.GetString(header.ToArray()).EndsWith("\r\n\r\n"))
                {
                    if(await stream.ReadAsync(one,timeout.Token)==0 || header.Length>16384)throw new IOException("Invalid smoke request.");
                    header.WriteByte(one[0]);
                }
                var line=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n").Single(x=>x.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase));
                var body=new byte[int.Parse(line.Split(':')[1].Trim())];await stream.ReadExactlyAsync(body,timeout.Token);requests.Add(JsonNode.Parse(body)!.AsObject());
                var delta=round==0 ? JsonNode.Parse("""{"role":"assistant","tool_calls":[{"index":0,"id":"smoke-key","type":"function","function":{"name":"keyboard_keys","arguments":"{}"}}]}""") : new JsonObject{["role"]="assistant",["content"]="Tâche exécutée."};
                var chunk=new JsonObject{["choices"]=new JsonArray(new JsonObject{["index"]=0,["delta"]=delta,["finish_reason"]=round==0?"tool_calls":"stop"}),["usage"]=new JsonObject{["prompt_tokens"]=42,["completion_tokens"]=10}};
                var bytes=Encoding.UTF8.GetBytes("data: "+chunk.ToJsonString()+"\n\ndata: [DONE]\n\n");
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"),timeout.Token);
                await stream.WriteAsync(bytes,timeout.Token);
            }
        },timeout.Token);
        try
        {
            using var store=new HarnessDb();
            var task=await store.ScheduledTasks.SingleAsync(timeout.Token);
            var target=await store.Providers.SingleAsync(x=>x.Id==task.ProviderId,timeout.Token);target.BaseUrl=endpoint;
            task.Enabled=false;task.EnabledSkills="keyboard_control";task.RememberHistory=true;task.ThinkingLevel="high";
            await store.SaveChangesAsync(timeout.Token);
            var first=await ExecuteScheduledTask(task,timeout.Token);
            if(first!="Terminée / Completed")throw new Exception("Scheduled execution failed: "+first);
            await store.Entry(task).ReloadAsync(timeout.Token);
            var second=await ExecuteScheduledTask(task,timeout.Token);
            if(second!="Terminée / Completed")throw new Exception("Scheduled continuation failed: "+second);
            await serve;
            var history=await store.Messages.AsNoTracking().Where(x=>x.ChatId==task.LastChatId).ToListAsync(timeout.Token);
            if(history.Count(x=>x.Role=="user")!=2 || !history.Any(x=>x.Role=="tool" && x.Content.Contains("ALT",StringComparison.OrdinalIgnoreCase)))
                throw new Exception("Scheduled tool selection/history not retained.");
            if(requests.Any(x=>x["model"]?.GetValue<string>()!=task.Model) || requests[2]["messages"]!.AsArray().Count(x=>x?["role"]?.GetValue<string>()=="user")!=2)
                throw new Exception("Scheduled API model/history mismatch.");
        }
        finally { timeout.Cancel(); server.Stop(); try { await serve; } catch(OperationCanceledException) { } }
    }
    static async Task Capture(FrameworkElement element,string path)
    {
#if !WINDOWS
        var target=new RenderTargetBitmap();await target.RenderAsync(element);
        var buffer=await target.GetPixelsAsync();var bytes=new byte[buffer.Length];using var reader=DataReader.FromBuffer(buffer);reader.ReadBytes(bytes);
        using var bitmap=new SkiaSharp.SKBitmap(target.PixelWidth,target.PixelHeight,SkiaSharp.SKColorType.Bgra8888,SkiaSharp.SKAlphaType.Premul);
        System.Runtime.InteropServices.Marshal.Copy(bytes,0,bitmap.GetPixels(),bytes.Length);
        using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var data=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);await File.WriteAllBytesAsync(path,data.ToArray());
#else
        await Task.CompletedTask;
#endif
    }
}
