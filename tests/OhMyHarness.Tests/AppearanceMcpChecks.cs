using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using System.Text;

static class AppearanceMcpChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        check(AppearanceThemes.All.Count == 6 && AppearanceThemes.All.Count(x=>x.Dark)==3, "Six thèmes, trois sombres et trois clairs");
        var settings = FeatureSettings.Read(new FeatureSettings { Theme="ivory", ComposerInfoExpanded=false }.Json());
        check(settings.Theme=="ivory" && !settings.ComposerInfoExpanded, "Thème et panneau replié persistés dans les réglages");
        const string godot = """
        {"mcpServers":{"godot":{"command":"npx","args":["@coding-solo/godot-mcp"],"env":{"GODOT_PATH":"/path/to/godot","DEBUG":"true"}}}}
        """;
        var parsed=McpConfigFile.Parse(godot).Single();
        check(parsed.Server.Name=="godot" && parsed.Server.Enabled && parsed.Server.ArgumentsJson.Contains("@coding-solo/godot-mcp") && McpSecrets.Parse(parsed.Secrets!).Environment["DEBUG"]=="true", "Format standard MCP et exemple Godot reconnus");
        bool bad=false;try{McpConfigFile.Parse("{\"mcpServers\":{\"bad\":{\"command\":\"npx\",\"env\":{\"DEBUG\":true}}}}");}catch{bad=true;}
        check(bad,"Valeurs env non textuelles rejetées");
        var folder=Path.Combine(Path.GetTempPath(),"omh-mcp-config-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var file=Path.Combine(folder,"MCP.json");
        await using var db=new HarnessDb(Path.Combine(folder,"database.sqlite"));await db.InitializeAsync();
        Task<byte[]> Encrypt(string s,CancellationToken ct)=>Task.FromResult(Encoding.UTF8.GetBytes("encrypted:"+s));
        db.McpServers.Add(new McpServer { Name="existing",Command="node",ProtectedSecrets=Encoding.UTF8.GetBytes("PRIVATE_STORED_SECRET") });await db.SaveChangesAsync();
        await McpConfigFile.SyncAsync(db,Encrypt,path:file);
        var initial=await File.ReadAllTextAsync(file);
        check(!initial.Contains("PRIVATE_STORED_SECRET") && !initial.Contains("env"),"Création MCP.json sans exporter les secrets SQLite");
        await McpConfigFile.SaveJsonAsync(db,godot,initial,Encrypt,path:file);
        check(await db.McpServers.CountAsync()==1 && (await db.McpServers.SingleAsync()).Name=="godot","JSON enregistré synchronisé avec SQLite");
        check(Encoding.UTF8.GetString((await db.McpServers.SingleAsync()).ProtectedSecrets).StartsWith("encrypted:"),"Secrets fournis dans le JSON passent par le chiffrement");
        var exported=McpConfigFile.Export(await db.McpServers.ToListAsync(),godot);
        check(exported.Contains("GODOT_PATH")&&!exported.Contains("encrypted:"),"Export conserve uniquement les valeurs déjà présentes dans le fichier");
        var protectedBefore=(await db.McpServers.SingleAsync()).ProtectedSecrets;
        await File.WriteAllTextAsync(file,godot+" ");
        await McpConfigFile.SyncAsync(db,(_,_)=>throw new Exception("Unchanged secret must not be encrypted again"),path:file, decrypt:(bytes,_)=>Task.FromResult(Encoding.UTF8.GetString(bytes)[10..]));
        check((await db.McpServers.SingleAsync()).ProtectedSecrets.SequenceEqual(protectedBefore),"Empreinte des permissions conservée pour des secrets inchangés");
        await File.WriteAllTextAsync(file,godot);
        var id=(await db.McpServers.SingleAsync()).Id;await McpConfigFile.SyncAsync(db,Encrypt,path:file);
        check((await db.McpServers.SingleAsync()).Id==id,"Synchronisation inchangée conserve les identifiants MCP");
        bad=false;try{await McpConfigFile.SaveJsonAsync(db,"{",godot,Encrypt,path:file);}catch{bad=true;}
        check(bad&&await File.ReadAllTextAsync(file)==godot&&await db.McpServers.CountAsync()==1,"JSON invalide ne modifie ni fichier ni serveurs");
        await File.WriteAllTextAsync(file,McpConfigFile.Empty);
        bad=false;try{await McpConfigFile.SaveJsonAsync(db,godot,godot,Encrypt,path:file);}catch(IOException){bad=true;}
        check(bad&&await File.ReadAllTextAsync(file)==McpConfigFile.Empty,"Modification externe protégée contre un écrasement silencieux");
        await McpConfigFile.SyncAsync(db,Encrypt,path:file);
        check(await db.McpServers.CountAsync()==0,"Suppression de serveurs sur disque prise en compte");
    }
}
