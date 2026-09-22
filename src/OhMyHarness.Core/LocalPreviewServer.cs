using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OhMyHarness.Core;

/// <summary>Loopback-only origin per approved folder, with the same path checks as the Windows preview.</summary>
public sealed class LocalPreviewServer : IDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback,0);
    readonly CancellationTokenSource lifetime=new();
    readonly string folder;
    public Uri Origin { get; }
    public LocalPreviewServer(string folder)
    {
        this.folder=folder; listener.Start();
        Origin=new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/"); _=ListenAsync();
    }
    async Task ListenAsync()
    {
        try { while(!lifetime.IsCancellationRequested) { var client=await listener.AcceptTcpClientAsync(lifetime.Token); _=ServeAsync(client); } }
        catch(OperationCanceledException) { } catch(SocketException) when(lifetime.IsCancellationRequested) { }
    }
    async Task ServeAsync(TcpClient client)
    {
        using(client)
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var stream=client.GetStream(); using var reader=new StreamReader(stream,Encoding.ASCII,leaveOpen:true);
                var request=await reader.ReadLineAsync(timeout.Token) ?? "";
                if(request.Length>8192) return;
                var parts=request.Split(' '); string? host=null; int size=request.Length;
                while(await reader.ReadLineAsync(timeout.Token) is { Length: >0 } line) { if((size+=line.Length)>16384)return; if(line.StartsWith("Host:",StringComparison.OrdinalIgnoreCase))host=line[5..].Trim(); }
                byte[] data; string headers;
                try
                {
                    if(parts.Length!=3 || parts[0]!="GET" || host!=Origin.Authority || !parts[1].StartsWith('/'))throw new UnauthorizedAccessException();
                    var path=LocalPreview.ResolveResource(folder,parts[1].Split('?')[0]);
                    if(new FileInfo(path).Length>32*1024*1024)throw new IOException("Preview file too large.");
                    data=await File.ReadAllBytesAsync(path,timeout.Token); headers=$"200 OK\r\nContent-Type: {LocalPreview.Mime(path)}";
                }
                catch { data=[]; headers="403 Forbidden"; }
                var head=Encoding.ASCII.GetBytes($"HTTP/1.1 {headers}\r\nContent-Length: {data.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head,timeout.Token); await stream.WriteAsync(data,timeout.Token);
            }
            catch(Exception ex) when(ex is IOException or OperationCanceledException or SocketException) { }
        }
    }
    public void Dispose() { lifetime.Cancel(); listener.Stop(); }
}
