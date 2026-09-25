using OhMyHarness.Core;
using SkiaSharp;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

static class AssetChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        async Task Throws<T>(Func<Task> action,string name) where T:Exception
        { try{await action();}catch(T){check(true,name);return;}throw new Exception("Expected "+typeof(T).Name+": "+name); }
        string root=Path.Combine(Path.GetTempPath(),"omh-assets-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string db=Path.Combine(root,"database.sqlite");var store=new AssetWorkspace(db,1);
            var doc=await store.CreateAsync("Test <safe>",128,128,"none",default);
            doc=await store.UpdateAsync(doc.Id,doc.Revision,d=>d.Layers[0].Shapes.Add(new() { Id="red",Type="rect",X=10,Y=10,Width=80,Height=80,Fill="#FF0000" }),default);
            doc=await store.UpdateAsync(doc.Id,doc.Revision,d=>d.Layers.Add(new() { Id="front",Name="Front",Shapes=[new(){Id="blue",Type="circle",X=50,Y=50,Radius=20,Fill="#0000FF"}] }),default);
            using(var image=SKBitmap.Decode(AssetRenderer.Export(doc,"png")))
            {check(image.GetPixel(0,0).Alpha==0,"Asset PNG preserves transparent canvas");check(image.GetPixel(50,50).Blue==255 && image.GetPixel(15,15).Red==255,"Layers compose front-to-back correctly");}
            var svg=AssetRenderer.Svg(doc);var xml=XDocument.Parse(svg);XNamespace ns="http://www.w3.org/2000/svg";
            check(xml.Root!.Element(ns+"title")!.Value=="Test <safe>" && xml.Root.Elements(ns+"g").Count()==2,"SVG escapes text and retains editable layer groups");
            var moved=doc.Clone();AssetTools.Edit(moved,new JsonArray(new JsonObject{["action"]="move_layer",["layer_id"]="front",["index"]=0}));
            using(var image=SKBitmap.Decode(AssetRenderer.Export(moved,"png")))check(image.GetPixel(50,50).Red==255,"Layer reorder changes rendered occlusion");
            var hidden=doc.Clone();hidden.Layers[1].Visible=false;
            using(var image=SKBitmap.Decode(AssetRenderer.Export(hidden,"png")))check(image.GetPixel(50,50).Red==255,"Hidden layer excluded from capture");
            var alpha=doc.Clone();alpha.Layers=[new(){Opacity=.5,Shapes=[new(){Id="green",Fill="#00FF0080",Width=128,Height=128}]}];
            using(var image=SKBitmap.Decode(AssetRenderer.Export(alpha,"png")))check(image.GetPixel(30,30).Alpha is >=63 and <=65,"Color alpha and layer opacity multiply correctly");
            var background=doc.Clone();background.Background="#FFFFFF";
            using(var image=SKBitmap.Decode(AssetRenderer.Export(background,"png",true)))check(image.GetPixel(0,0).Alpha==0,"Transparent export omits canvas background");
            using(var image=SKBitmap.Decode(AssetRenderer.Export(background,"png")))check(image.GetPixel(0,0)==SKColors.White,"Opaque export includes canvas background");
            using(var image=SKBitmap.Decode(AssetRenderer.Export(doc,"webp")))check(image.GetPixel(0,0).Alpha==0,"WebP export preserves transparency");
            using(var image=SKBitmap.Decode(AssetRenderer.Export(doc,"jpeg",background:"#FFFFFF",scale:2)))check(image.Width==256 && image.Height==256 && image.GetPixel(0,0).Alpha==255,"JPEG exports at requested scale with matte");
            check(Encoding.ASCII.GetString(AssetRenderer.Export(doc,"pdf"),0,5)=="%PDF-","Vector PDF export created");
            await Throws<ArgumentException>(()=>Task.Run(()=>AssetRenderer.Export(doc,"jpeg",true)),"JPEG transparency explicitly rejected");
            await Throws<ArgumentException>(()=>Task.Run(()=>AssetRenderer.Export(new(){Width=4096,Height=4096},"png",scale:4)),"Raster export memory cap enforced");
            using(var image=SKBitmap.Decode(AssetRenderer.Preview(doc,64)))check(image.Width==64 && image.Height==64,"Capture size limit preserves aspect and dimensions");
            var persisted=await new AssetWorkspace(db,1).ReadAsync(doc.Id,default);
            check(persisted.Revision==3 && persisted.Layers[1].Shapes[0].Id=="blue","Drawings persist across workspace reloads");
            check((await new AssetWorkspace(db,2).ListAsync(default)).Count==0,"Conversations have isolated assets");
            await Throws<InvalidOperationException>(()=>store.UpdateAsync(doc.Id,1,d=>d.Name="stale",default),"Stale edits rejected without overwriting user changes");
            await Throws<ArgumentException>(()=>store.UpdateAsync(doc.Id,doc.Revision,d=>{d.Name="bad";d.Layers[0].Shapes[0].Fill="url(https://example.com)";},default),"Remote SVG paint references rejected");
            check((await store.ReadAsync(doc.Id,default)).Name==doc.Name,"Invalid operation leaves the original document unchanged");
            await Throws<ArgumentException>(()=>store.ReadAsync("../escape",default),"Asset path traversal blocked");
            var text=doc.Clone();text.Layers[0].Shapes.Add(new(){Id="text",Type="text",Text="<script>alert(1)</script>",X=5,Y=100,Fill="#FFFFFF"});
            check(!AssetRenderer.Svg(text).Contains("<script>"),"SVG text cannot inject markup");
            var path=doc.Clone();path.Layers=[new(){Shapes=[new(){Id="curve",Type="path",Path="M 10 10 C 20 0 80 0 90 10 L 90 90 L 10 90 Z",Fill="#FF00FF"}]}];
            using(var image=SKBitmap.Decode(AssetRenderer.Export(path,"png")))check(image.GetPixel(50,50).Red==255 && image.GetPixel(50,50).Blue==255,"Arbitrary SVG curve paths render");
            foreach(string format in new[]{"svg","png","webp","jpeg","pdf"})
            {var file=await store.ExportAsync(doc,format,false,"#FFFFFF",1,default);check(File.Exists(file)&&new FileInfo(file).Length>10,"Export saved safely: "+format);}
            using var run=new ConversationSession(new(){Id=1,ExecutionMode="execute"},new(),new(){SupportsImages=true},new(){EnabledSkills=AssetTools.SkillId},"",[],db);
            string enabled=AssetTools.SkillId;bool allowed=true;int asks=0;
            Task<AssetTools.Result> Tool(string name,JsonObject args)=>AssetTools.CallAsync(run,name,args,_=>Task.FromResult(enabled),(_,_,_,_)=>{asks++;return Task.FromResult(allowed);},default);
            var capture=await Tool("asset_capture",new(){["asset_id"]=doc.Id});
            check(capture.Image?.Mime=="image/png" && asks==1,"Capture returns an image attachment after approval");
            check(capture.Image!.Data.Length>0 && JsonNode.Parse(capture.Text)!["capture_width"]!.GetValue<int>()==128,"Capture metadata identifies artwork coordinates");
            allowed=false;var denied=await Tool("asset_edit",new(){["asset_id"]=doc.Id,["expected_revision"]=doc.Revision,["operations"]=new JsonArray(new JsonObject{["action"]="delete_layer",["layer_id"]="front"})});
            check(denied.Text.Contains("denied") && (await store.ReadAsync(doc.Id,default)).Revision==3,"Denied asset edit leaves drawing unchanged");
            enabled="";await Throws<UnauthorizedAccessException>(()=>Tool("asset_capture",new(){["asset_id"]=doc.Id}),"Disabled skill blocks capture");
            enabled=AssetTools.SkillId;allowed=true;run.Chat.ExecutionMode="plan";
            await Throws<UnauthorizedAccessException>(()=>Tool("asset_create",new(){["name"]="blocked"}),"Plan mode blocks canvas writes");
            check((await Tool("asset_inspect",new(){["asset_id"]=doc.Id})).Text.Contains("Test"),"Plan mode permits asset inspection");
            run.Chat.SandboxEnabled=true;await Throws<UnauthorizedAccessException>(()=>Tool("asset_inspect",new(){["asset_id"]=doc.Id}),"Offline sandbox cannot access host assets");
            var tools=new JsonArray();AssetTools.AddDefinitions(tools,AssetTools.SkillId);check(tools.Count==5,"Asset skill exposes five shared GUI/CLI tools");
            var none=new JsonArray();AssetTools.AddDefinitions(none,"");check(none.Count==0,"Disabled skill hides all asset definitions");
        }
        finally {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    }
}
