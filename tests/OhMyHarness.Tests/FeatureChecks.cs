using OhMyHarness.Core;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

static class FeatureChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var root=Path.Combine(Path.GetTempPath(),"omh-features-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var sources=Path.Combine(root,"sources");Directory.CreateDirectory(sources);
            var arbitrary=Path.Combine(sources,"script.gd");await File.WriteAllTextAsync(arbitrary,"extends Node\nfunc _ready():\n    print('hello')");
            var access=new SourceAccess(sources);
            check((await access.ReadAsync("script.gd",default,2,3)).Contains("2: func"),"Lecture des extensions arbitraires avec plage de lignes");
            await File.WriteAllBytesAsync(Path.Combine(sources,"data.bin"),[0,1,2,255]);
            check((await access.ReadAsync("data.bin",default)).Contains("000102FF"),"Lecture binaire rendue en hexadécimal");
            await File.WriteAllTextAsync(Path.Combine(sources,"LICENSE"),"license text");
            check(await access.ReadAsync("LICENSE",default)=="license text","Lecture des fichiers sans extension");
            var tokens=CodeHighlight.Tokens("const text = \"<script>\"; // comment","javascript").ToList();
            check(string.Concat(tokens.Select(x=>x.Text))=="const text = \"<script>\"; // comment" && tokens.Select(x=>x.Color).Distinct().Count()>2,"Coloration conserve le code et distingue les catégories");
            check(!CodeHighlight.Html("<pre><code class=\"language-js\">&lt;script&gt;</code></pre>").Contains("<script>"),"Coloration HTML échappe les balises de code");
            var a=LocalEmbeddings.Embed("A friendly dog is playing with a puppy.");
            var b=LocalEmbeddings.Embed("A pet dog and a puppy play together.");
            var c=LocalEmbeddings.Embed("SQL database tables use indexes for fast queries.");
            double Dot(float[] x,float[] y)=>x.Select((v,i)=>(double)v*y[i]).Sum();
            check(a.Length==384 && Math.Abs(Dot(a,a)-1)<.001 && Dot(a,b)>Dot(a,c),"Vrai MiniLM local : vecteurs normalisés et proximité sémantique");
            // Golden ids from the pinned Hugging Face tokenizer.json with Transformers.js.
            check(LocalEmbeddings.Tokenize("Bonjour, où sont les paramètres de sécurité ?").SequenceEqual(new long[] {0,84602,4,11497,2045,199,121,93605,7,8,43732,705,2}),"Tokenizer français conforme à la référence : accents et ponctuation");
            check(LocalEmbeddings.Tokenize("L’utilisateur réinitialise son mot de passe oublié.").SequenceEqual(new long[] {0,339,26,139133,3537,73,40705,12811,775,2080,8,10922,185889,5,2}),"Tokenizer français conforme : apostrophe typographique");
            check(LocalEmbeddings.Tokenize("École français déjà Noël").SequenceEqual(new long[] {0,3050,46813,43054,15154,114589,2}),"Tokenizer préserve la casse et les accents français");
            check(LocalEmbeddings.Tokenize("A friendly dog is playing with a puppy.").SequenceEqual(new long[] {0,62,101786,10269,83,75169,678,10,207109,5,2}),"Tokenizer anglais reste conforme à la référence");
            check(LocalEmbeddings.Tokenize("École").SequenceEqual(LocalEmbeddings.Tokenize("E\u0301cole")),"Tokenizer normalise les accents Unicode composés et décomposés");
            var longTokens=LocalEmbeddings.Tokenize(string.Join(' ',Enumerable.Repeat("bonjour",300)));
            check(longTokens.Length==128 && longTokens[0]==0 && longTokens[^1]==2,"Tokenizer borne la fenêtre du modèle en conservant BOS et EOS");
            var frenchDog=LocalEmbeddings.Embed("Un chien joue avec un petit chiot dans le jardin.");
            check(Dot(frenchDog,a)>Dot(frenchDog,c),"MiniLM multilingue rapproche le français et l'anglais par le sens");
            await File.WriteAllTextAsync(Path.Combine(sources,"database.custom"),"SQL database tables store records. Indexes accelerate queries and database searches.");
            await File.WriteAllTextAsync(Path.Combine(sources,"pets.custom"),"Dogs and puppies are pets. Feed your dog and take it on walks.");
            await File.WriteAllTextAsync(Path.Combine(sources,"connexion.md"),"Si vous avez oublié votre mot de passe, cliquez sur le lien de réinitialisation. Un courriel permet de récupérer l’accès à votre compte et de choisir un nouveau mot de passe.");
            await File.WriteAllTextAsync(Path.Combine(sources,"cuisine.md"),"Pour préparer une tarte aux pommes, étalez la pâte, découpez les fruits et faites cuire au four pendant trente minutes.");
            var database=Path.Combine(root,"test.sqlite");
            await using var db=new HarnessDb(database);await db.InitializeAsync();await db.InitializeAsync();
            var project=await db.Projects.FirstAsync();project.SetSourceFolders([sources]);
            var state=await db.States.SingleAsync();state.EnabledSkills="rag,sources";state.FeaturesJson=new FeatureSettings {RagTopK=2}.Json();await db.SaveChangesAsync();
            var chat=await db.Chats.FirstAsync();var provider=await db.Providers.FirstAsync();
            using var run=new ConversationSession(chat,project,provider,state,"test",[],database);
            db.RagChunks.Add(new() {ProjectId=project.Id,Model="minilm-l6-v2-afdb6f1a-v1",Path=arbitrary,Hash="old",StartLine=1,EndLine=1,Text="old index",Vector=new byte[384*4]});await db.SaveChangesAsync();
            var oldSearch=await RagTools.CallAsync(run,"rag_search",new(){["query"]="mot de passe"},(_,_)=>Task.FromResult(""),(_,_,_,_)=>Task.FromResult(false),default);
            check(oldSearch.Contains("rag_index"),"Ancien index anglais non réutilisé : demande de réindexation");
            var indexed=await RagTools.CallAsync(run,"rag_index",[],(_,_)=>Task.FromResult(""),(_,_,_,_)=>Task.FromResult(false),default);
            check(JsonNode.Parse(indexed)?["chunks"]?.GetValue<int>()>0,"RAG local indexe les fichiers sans transmission API");
            check(await db.RagChunks.AllAsync(x=>x.Model==LocalEmbeddings.ModelId),"Réindexation remplace les anciens vecteurs par le modèle multilingue");
            var result=await RagTools.CallAsync(run,"rag_search",new(){["query"]="How do SQL database indexes speed up queries?"},(_,_)=>Task.FromResult(""),(_,_,_,_)=>Task.FromResult(false),default);
            check(JsonNode.Parse(result)?["hits"]?[0]?["path"]?.GetValue<string>().EndsWith("database.custom")==true,"RAG retrouve le bon fichier par similarité avec citations");
            async Task<bool> FirstHit(string query,string file) {
                var response=await RagTools.CallAsync(run,"rag_search",new(){["query"]=query},(_,_)=>Task.FromResult(""),(_,_,_,_)=>Task.FromResult(false),default);
                return JsonNode.Parse(response)?["hits"]?[0]?["path"]?.GetValue<string>().EndsWith(file)==true;
            }
            check(await FirstHit("Comment récupérer l’accès à mon compte si j’ai perdu mon mot de passe ?","connexion.md"),"RAG retrouve un document français avec une question française");
            check(await FirstHit("How can I reset my forgotten password and recover my account?","connexion.md"),"RAG retrouve un document français avec une question anglaise");
            check(await FirstHit("Comment accélérer les requêtes dans une base de données grâce aux index ?","database.custom"),"RAG retrouve un document anglais avec une question française");
            await File.WriteAllTextAsync(Path.Combine(sources,"database.custom"),"Changed version");
            result=await RagTools.CallAsync(run,"rag_search",new(){["query"]="SQL database"},(_,_)=>Task.FromResult(""),(_,_,_,_)=>Task.FromResult(false),default);
            check(!result.Contains("database.custom"),"RAG exclut les passages périmés après modification");
            var definitions=new JsonArray();RagTools.AddDefinitions(definitions,true,"rag");AgentPolicy.Filter(definitions,"plan");
            check(!definitions.Any(x=>x?["function"]?["name"]?.GetValue<string>()=="rag_index") && definitions.Count==3,"Plan autorise la consultation RAG mais pas la reconstruction");
            var browserTools=ChatEngine.ToolDefinitions(false,true,false);new FeatureSettings {BrowserMode="chrome"}.FilterBrowser(browserTools);
            check(browserTools.Count==0,"Mode Chrome retire les outils WebView2");
            check(new FeatureSettings{BrowserMode="chrome"}.ChromeServer(1).ArgumentsJson!=new FeatureSettings{BrowserMode="chrome"}.ChromeServer(2).ArgumentsJson,"Chrome MCP possède un profil par conversation");
        }
        finally {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    }
}
