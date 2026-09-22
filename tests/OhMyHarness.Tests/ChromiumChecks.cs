using OhMyHarness.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

static class ChromiumChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var folder=Path.Combine(Path.GetTempPath(),"omh-chromium-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        PortableStorage.UseDatabase(Path.Combine(folder,"database.sqlite"));
        await File.WriteAllTextAsync(Path.Combine(folder,"page.html"),"<html><title>Uno browser</title><body><button onclick=\"this.textContent='Clicked'\">Click</button><input id='entry'></body></html>");
        using var server=new LocalPreviewServer(folder);using var first=new ChromiumBrowser();using var second=new ChromiumBrowser();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await first.StartAsync(1,"",timeout.Token,true);await second.StartAsync(2,"",timeout.Token,true);
        var page=new Uri(server.Origin,"page.html");await first.NavigateAsync(page,timeout.Token);await second.NavigateAsync(page,timeout.Token);
        check(await first.EvaluateAsync("document.title")=="\"Uno browser\"","Chromium : navigation et lecture JavaScript");
        check(first.Source == page.AbsoluteUri,"Chromium : origine courante utilisée pour les autorisations");
        await first.EvaluateAsync("history.pushState({},'', '#details')");
        for (var attempt=0;attempt<20 && !first.Source.EndsWith("#details");attempt++) await Task.Delay(25);
        check(first.Source.EndsWith("#details"),"Chromium : adresse suivie après navigation interne à la page");
        await first.EvaluateAsync("localStorage.setItem('scope','first');document.cookie='scope=first';document.querySelector('button').click()");
        check(await second.EvaluateAsync("localStorage.getItem('scope')")=="null" && await second.EvaluateAsync("document.cookie")=="\"\"","Chromium : profils, cookies et stockage isolés entre conversations");
        check(await first.EvaluateAsync("document.querySelector('button').textContent")=="\"Clicked\"","Chromium : interaction DOM");
        await first.EvaluateAsync("document.querySelector('input').focus()");await first.CallAsync("Input.insertText",new(){["text"]="multiline\ninput"});
        check((await first.EvaluateAsync("document.querySelector('input').value")).Contains("multiline"),"Chromium : saisie clavier CDP");
        var image=await first.CallAsync("Page.captureScreenshot",new(){["format"]="png"});var bytes=Convert.FromBase64String(image["data"]!.GetValue<string>());
        check(bytes.Length>1000 && bytes[0]==137,"Chromium : capture PNG du navigateur");
        using var http=new HttpClient();var blocked=await http.GetAsync(new Uri(server.Origin,".env"));check(blocked.StatusCode==System.Net.HttpStatusCode.Forbidden,"Aperçu local : fichiers exclus refusés");
        using var request=new HttpRequestMessage(HttpMethod.Get,page);request.Headers.Host="attacker.invalid";using var response=await http.SendAsync(request);check(response.StatusCode==System.Net.HttpStatusCode.Forbidden,"Aperçu local : Host étranger refusé");
    }
}
