using System.Runtime.InteropServices;

namespace OhMyHarness.Core;

public static class MacDesktop
{
    const string CG="/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    [StructLayout(LayoutKind.Sequential)] struct Point { public double X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct Size { public double Width,Height; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public Point Origin; public Size Size; }
    [DllImport(CG)] static extern int CGGetActiveDisplayList(uint count, [Out] uint[] displays, out uint actual);
    [DllImport(CG)] static extern uint CGMainDisplayID();
    [DllImport(CG)] static extern Rect CGDisplayBounds(uint display);
    [DllImport(CG)] static extern bool CGPreflightScreenCaptureAccess();
    [DllImport(CG)] static extern nint CGEventCreate(nint source);
    [DllImport(CG)] static extern Point CGEventGetLocation(nint ev);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] static extern void CFRelease(nint value);
    public static (double X,double Y) Cursor()
    {
        var ev=CGEventCreate(0);try { var p=CGEventGetLocation(ev);return(p.X,p.Y); } finally { if(ev!=0)CFRelease(ev); }
    }
    public static IReadOnlyList<ScreenInfo> Screens()
    {
        if(!OperatingSystem.IsMacOS())throw new PlatformNotSupportedException();
        var ids=new uint[32];if(CGGetActiveDisplayList(32,ids,out var count)!=0)throw new IOException("Écrans macOS indisponibles.");
        return ids.Take((int)count).Select((id,index)=> { var r=CGDisplayBounds(id);return new ScreenInfo(index,id.ToString(),id==CGMainDisplayID(),(int)r.Origin.X,(int)r.Origin.Y,(int)r.Size.Width,(int)r.Size.Height); }).ToArray();
    }
    public static void DemandScreenCapture()
    {
        if(!CGPreflightScreenCaptureAccess())throw new UnauthorizedAccessException("Autorisez OhMyHarness dans Réglages système → Confidentialité → Enregistrement de l’écran, puis relancez l’application.");
    }
}
