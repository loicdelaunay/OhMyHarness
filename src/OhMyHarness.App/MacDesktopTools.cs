using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using OhMyHarness.Core;
using OhMyHarness.Core.Hosting;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async Task<string> MacInput(string tool, JsonObject args, string? windowId, CancellationToken ct)
    {
        var application=windowId==null ? null : DesktopApplications.Resolve(windowId);
        var target=application?.ProcessId ?? DesktopInput.Foreground();
        if(!await RequestAccessAsync(tool=="desktop_mouse"?"desktop|mouse":"desktop|keyboard", "Contrôle du bureau / Desktop input", args.ToJsonString()+(application==null?"":"\n"+application.Application+" · "+application.Title), "macOS",ct))return "Accès refusé / Access denied";
        DesktopInput.Restore(target);await Task.Delay(180,ct);
        if(application!=null)
        {
            application=DesktopApplications.Resolve(windowId!);var point=application.RelativePoint(args["x"]!.GetValue<double>(),args["y"]!.GetValue<double>());
            DesktopApplications.DemandPointTarget(application,point.X,point.Y);args["x"]=point.X;args["y"]=point.Y;
        }
        ct.ThrowIfCancellationRequested();DesktopInput.Execute(tool,args);return "Action exécutée / Action completed";
    }
    Task<string> MacScreens()
    {
        var screens=MacDesktop.Screens();
        return Task.FromResult(JsonSerializer.Serialize(new { screens=screens.Select(s=>new {index=s.Index,name=s.Name,is_primary=s.IsPrimary,x=s.X,y=s.Y,width=s.Width,height=s.Height}) }));
    }
#if !WINDOWS
    async Task<string> MacScreenshot(string? screen,int? x,int? y,int? width,int? height,int? maxWidth,int? maxHeight,CancellationToken ct,Provider? targetProvider,string? windowId)
    {
        if((targetProvider ?? provider)?.SupportsImages!=true && !VisionBridge.Enabled(ToolSkills))return "Le modèle actif n’accepte pas les images.";
        var screens=MacDesktop.Screens();var minX=screens.Min(s=>s.X);var minY=screens.Min(s=>s.Y);var fullWidth=screens.Max(s=>s.X+s.Width)-minX;var fullHeight=screens.Max(s=>s.Y+s.Height)-minY;
        var application=windowId==null?null:DesktopApplications.Resolve(windowId);
        if(application!=null && (screen!=null || x.HasValue || y.HasValue || width.HasValue || height.HasValue))throw new ArgumentException("window_id cannot be combined with screen/crop.");
        var region=application==null?ScreenGeometry.ResolveRegion(screens,screen,x,y,width,height,minX,minY,fullWidth,fullHeight):((int)application.X,(int)application.Y,(int)application.Width,(int)application.Height);
        if(application!=null)DesktopApplications.ValidateCapture(application);
        var foreground=DesktopInput.Foreground();var pointer=MacDesktop.Cursor();
        if(!await RequestAccessAsync("desktop|screenshot", "Capture d’écran / Screenshot", application?.Title ?? $"{region}", "Capture macOS",ct))return "Accès refusé / Access denied";
        MacDesktop.DemandScreenCapture();DesktopInput.Restore(foreground);await Task.Delay(180,ct);
        if(windowId!=null) { application=DesktopApplications.Resolve(windowId);DesktopApplications.ValidateCapture(application);region=((int)application.X,(int)application.Y,(int)application.Width,(int)application.Height); }
        var folder=Path.Combine(PortableStorage.Root,"Captures");Directory.CreateDirectory(folder);var path=Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");
        try
        {
            var start=new ProcessStartInfo("/usr/sbin/screencapture") { UseShellExecute=false,RedirectStandardError=true };
            foreach(var arg in new[]{"-x","-o","-t","png"})start.ArgumentList.Add(arg);
            if(application!=null) { start.ArgumentList.Add("-l");start.ArgumentList.Add(application.NativeId.ToString()); }
            else { start.ArgumentList.Add("-R");start.ArgumentList.Add($"{region.Item1},{region.Item2},{region.Item3},{region.Item4}"); }
            start.ArgumentList.Add(path);
            using var process=Process.Start(start) ?? throw new IOException("Capture indisponible.");
            try { await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(20),ct); }
            catch { if(!process.HasExited)process.Kill(true);throw; }
            if(process.ExitCode!=0)throw new IOException(await process.StandardError.ReadToEndAsync(ct));
            using var bitmap=SkiaSharp.SKBitmap.Decode(path) ?? throw new IOException("Image vide.");
            // Draw the cursor recorded before the approval popup in window/display coordinates.
            var px=(float)((pointer.X-region.Item1)*bitmap.Width/region.Item3);var py=(float)((pointer.Y-region.Item2)*bitmap.Height/region.Item4);
            if(px>=0 && py>=0 && px<bitmap.Width && py<bitmap.Height)
            {
                using var canvas=new SkiaSharp.SKCanvas(bitmap);using var paint=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.White,IsAntialias=true};using var cursor=new SkiaSharp.SKPath();
                cursor.MoveTo(px,py);cursor.LineTo(px,py+20);cursor.LineTo(px+6,py+14);cursor.LineTo(px+14,py+14);cursor.Close();canvas.DrawPath(cursor,paint);paint.Style=SkiaSharp.SKPaintStyle.Stroke;paint.Color=SkiaSharp.SKColors.Black;paint.StrokeWidth=1;canvas.DrawPath(cursor,paint);
            }
            var (w,h)=ScreenGeometry.CalculateScaledDimensions(bitmap.Width,bitmap.Height,maxWidth,maxHeight);
            using var resized=bitmap.Resize(new SkiaSharp.SKImageInfo(w,h),new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear));using var image=SkiaSharp.SKImage.FromBitmap(resized);using var data=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
            pendingToolScreenshot=data.ToArray();pendingToolScreenshotMime="image/png";pendingToolScreenshotWidth=w;pendingToolScreenshotHeight=h;pendingToolScreenshotLabel="Capture macOS · "+(application?.Title ?? screen ?? "Écran");
            return JsonSerializer.Serialize(new {ok=true,window_id=windowId,captured_region=new{x=region.Item1,y=region.Item2,width=region.Item3,height=region.Item4},image=new{width=w,height=h}});
        }
        finally { if(File.Exists(path))File.Delete(path); }
    }
#endif
}
