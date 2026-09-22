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
            if(modelSelector.Items.OfType<ModelChoice>().Count(x=>x.ProviderId==fixture.Id)!=2)throw new Exception("Model picker did not refresh.");
            var bubble=new StackPanel();MarkdownRenderer.RenderTo(bubble,"# Uno Platform\n\n**Texte sélectionnable** avec [un lien](https://example.com).\n\n```csharp\nvar task = new ScheduledTask();\n```\n\n- [x] Modèles\n- [ ] Tâches");messages.Children.Add(bubble);
            await Task.Delay(500);
            await Capture(root,Path.Combine(output,"chat.png"));
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
            Descendants(editor.Panel).OfType<CheckBox>().Single(x=>x.Content?.ToString()=="model-b").IsChecked=false;
            editor.Commit();await SaveProviderDraftsAsync(editor);PopulateModelSelector();
            if(modelSelector.Items.OfType<ModelChoice>().Where(x=>x.ProviderId==fixture.Id).Select(x=>x.Model).Single()!="model-a")
                throw new Exception("Provider card checkbox did not update the model picker.");
            File.WriteAllText(Path.Combine(output,"smoke-ok.txt"),"Uno UI: startup, Markdown, model picker, task editor/save, scheduled generation/tool/history and provider cards passed.");
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
