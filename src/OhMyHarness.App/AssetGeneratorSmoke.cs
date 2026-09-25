using Microsoft.UI.Xaml;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeAssetGenerator(string output)
    {
        var fixture=new Project { Name="Asset studio",Chats=[new(){Title="Créer un badge vectoriel"},new(){Title="Conversation sans dessin"}] };
        db.Projects.Add(fixture);await db.SaveChangesAsync();projects.ItemsSource=new[]{fixture};projects.SelectedItem=fixture;await SelectProject();
        state.EnabledSkills=AssetTools.SkillId;state.PermissionMode="allow";
        var features=FeatureSettings.Read(state.FeaturesJson);features.AutoFocusTool=true;state.FeaturesJson=features.Json();await db.SaveChangesAsync();
        var owner=chat!;
        using var run=new ConversationRun(owner,fixture,new Provider { SupportsImages=true },state,"Draw a badge",[]) { Messages=messages };
        async Task<JsonNode> Tool(string name,JsonObject args)
        {
            var call=new JsonObject { ["function"]=new JsonObject { ["name"]=name,["arguments"]=args.ToJsonString() } };
            return JsonNode.Parse(await RunTool(call,new SourceAccess([]),run,CancellationToken.None))!;
        }
        var created=await Tool("asset_create",new(){["name"]="Orbit · SVG badge",["width"]=640,["height"]=480});
        string id=created["asset_id"]!.GetValue<string>();
        JsonObject Shape(string layer,AssetShape s)=>new(){["action"]="shape",["layer_id"]=layer,["shape"]=System.Text.Json.JsonSerializer.SerializeToNode(s,AssetDocument.Json)};
        var ops=new JsonArray(
            new JsonObject{["action"]="layer",["layer_id"]="layer-1",["name"]="Carte"},
            Shape("layer-1",new(){Id="card",Type="rect",X=40,Y=35,Width=560,Height=410,Radius=36,Fill="#111827"}),
            new JsonObject{["action"]="layer",["layer_id"]="orbit",["name"]="Orbite cyan"},
            Shape("orbit",new(){Id="ring",Type="ellipse",X=175,Y=115,Width=290,Height=145,Fill="none",Stroke="#4CC9F0",StrokeWidth=12,Rotation=0}),
            Shape("orbit",new(){Id="planet",Type="circle",X=320,Y=187,Radius=78,Fill="#7C3AED"}),
            Shape("orbit",new(){Id="highlight",Type="circle",X=293,Y=161,Radius=20,Fill="#FFFFFF55"}),
            Shape("orbit",new(){Id="moon",Type="circle",X=458,Y=179,Radius=16,Fill="#FFCD70"}),
            new JsonObject{["action"]="layer",["layer_id"]="type",["name"]="Typographie"},
            Shape("type",new(){Id="title",Type="text",X=202,Y=344,Text="ORBIT",FontSize=64,Bold=true,Fill="#FFFFFF"}),
            Shape("type",new(){Id="subtitle",Type="text",X=179,Y=387,Text="BUILT WITH SHAPES",FontSize=23,Fill="#A8B6CD"}));
        await Tool("asset_edit",new(){["asset_id"]=id,["expected_revision"]=1,["operations"]=ops});
        if(toolTabs.SelectedIndex!=4||!browserVisible||selectedAsset?.Revision!=2||assetImage.Source==null||assetLayers.Children.Count!=3)throw new Exception("Live asset preview did not update.");
        var captured=await Tool("asset_capture",new(){["asset_id"]=id});
        var screenshot=TakePendingToolScreenshot();if(screenshot==null||screenshot.Value.Data.Length==0)throw new Exception("Asset screenshot not attached to tool response.");
        await File.WriteAllBytesAsync(Path.Combine(output,"asset-capture.png"),screenshot.Value.Data);
        await Tool("asset_export",new(){["asset_id"]=id,["format"]="svg",["transparent"]=true});
        await Task.Delay(150);await Capture(root,Path.Combine(output,"asset-studio.png"));
        // User changes a layer while the agent still has the preceding revision.
        var store=CurrentAssets();await store.UpdateAsync(id,2,d=>d.Layers[1].Visible=false,default);await RefreshAssetsAsync(id);
        if(selectedAsset?.Revision!=3)throw new Exception("User layer change did not refresh.");
        var other=allProjectChats.Single(c=>c.Id!=owner.Id);chats.SelectedItem=other;await SelectChat();
        if(selectedAsset!=null||assetImage.Source!=null)throw new Exception("Asset preview leaked into another conversation.");
        chats.SelectedItem=allProjectChats.Single(c=>c.Id==owner.Id);await SelectChat();
        if(selectedAsset?.Id!=id||selectedAsset.Revision!=3)throw new Exception("Asset was not restored on conversation switch.");
        await store.UpdateAsync(id,3,d=>d.Layers[1].Visible=true,default);await RefreshAssetsAsync(id);
        var pixel=await Tool("asset_create",new(){["name"]="Animated pixel sprite",["width"]=64,["height"]=64,["pixel_size"]=8});
        var pixelId=pixel["asset_id"]!.GetValue<string>();
        await Tool("asset_edit",new(){["asset_id"]=pixelId,["expected_revision"]=1,["operations"]=new JsonArray(
            new JsonObject{["action"]="pixel_rect",["layer_id"]="layer-1",["x"]=2,["y"]=2,["width"]=3,["height"]=3,["color"]="#4CC9F0"},
            new JsonObject{["action"]="frame_add",["frame_id"]="first",["duration_ms"]=80},
            new JsonObject{["action"]="frame_add",["frame_id"]="second",["source_frame_id"]="first",["duration_ms"]=140},
            new JsonObject{["action"]="pixel",["frame_id"]="second",["layer_id"]="layer-1",["x"]=2,["y"]=2,["color"]="#FF0044"})});
        if(selectedAsset?.Id!=pixelId||assetFramePicker.Items.Count!=3||assetImage.Source==null)throw new Exception("Pixel art animation did not appear in the GUI.");
        assetFramePicker.SelectedIndex=2;await Task.Delay(200);
        if(selectedAssetFrame!=1||assetGrid.IsChecked==true)throw new Exception("Animation frame selection failed.");
        var exported=await Tool("asset_export",new(){["asset_id"]=pixelId,["format"]="gif"});
        if(!File.Exists(exported["path"]!.GetValue<string>()))throw new Exception("Animated GIF export failed in GUI runtime.");
        CloseConversationBrowser(owner.Id);
        var firstTab=NewBrowserTab(owner.Id);var secondTab=NewBrowserTab(owner.Id);
        if(conversationBrowsers.Keys.Count(k=>k.ChatId==owner.Id)!=2||SelectedBrowserTab(owner.Id)!=secondTab.TabId)throw new Exception("Web tabs did not open independently.");
        await ShowToolAsync(0);await Task.Delay(100);await Capture(root,Path.Combine(output,"web-tabs.png"));
        SelectBrowserTab(owner.Id,firstTab.TabId);CloseBrowserTab(owner.Id,firstTab.TabId);
        if(SelectedBrowserTab(owner.Id)!=secondTab.TabId)throw new Exception("Closing a Web tab lost the remaining tab.");
        CloseConversationBrowser(owner.Id);
        if(conversationBrowsers.Keys.Any(k=>k.ChatId==owner.Id))throw new Exception("Web tabs leaked after conversation close.");
        await ShowToolAsync(4);
        foreach(var theme in new[]{"fluent-dark","fluent-light"})
        {FluentDesign.SetTheme(theme);await Task.Delay(100);await Capture(root,Path.Combine(output,"asset-studio-"+theme+".png"));}
        File.WriteAllText(Path.Combine(output,"smoke-ok.txt"),"Asset tools, pixel frames and GIF, live preview, auto-focus, layer refresh, conversation isolation, and Web tabs passed.");
    }
}
