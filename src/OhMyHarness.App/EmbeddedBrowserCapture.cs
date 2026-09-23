using System.Diagnostics;
using System.Runtime.InteropServices;
using OhMyHarness.Core;
using SkiaSharp;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
#if !WINDOWS
    async Task<byte[]> CaptureEmbeddedBrowserDesktopAsync(int width, int height, CancellationToken ct)
    {
        // Uno does not implement CoreWebView2.CapturePreviewAsync. Capture only
        // the visible native WebView, never an unrelated desktop region.
        if (!browserVisible || toolTabs.SelectedIndex != 0 || chat?.Id != CurrentBrowser.Id ||
            browser.ActualWidth < 1 || browser.ActualHeight < 1)
            throw new IOException("Affichez l’onglet Web de cette conversation pour capturer le navigateur intégré.");

        var own = DesktopApplications.List().Where(w => w.ProcessId == Environment.ProcessId && w.Visible && !w.Minimized)
            .OrderBy(w => Math.Abs(w.Width - root.ActualWidth) + Math.Abs(w.Height - root.ActualHeight)).FirstOrDefault()
            ?? throw new IOException("Fenêtre de l’application introuvable pour la capture du navigateur.");
        var local = browser.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));
        SKBitmap cropped;
        if (OperatingSystem.IsWindows())
        {
            using var dpi = DesktopApplications.PhysicalCoordinates();
            DesktopApplications.ValidateCapture(own);
            var client = DesktopInterop.ClientArea((nint)own.NativeId);
            var sx = client.Width / Math.Max(1, root.ActualWidth);
            var sy = client.Height / Math.Max(1, root.ActualHeight);
            var x = client.X - (int)own.X + (int)Math.Round(local.X * sx);
            var y = client.Y - (int)own.Y + (int)Math.Round(local.Y * sy);
            var w = Math.Max(1, (int)Math.Round(browser.ActualWidth * sx));
            var h = Math.Max(1, (int)Math.Round(browser.ActualHeight * sy));
            if (x < 0 || y < 0 || x + w > own.Width || y + h > own.Height)
                throw new IOException("Zone du navigateur hors de la fenêtre.");
            ct.ThrowIfCancellationRequested();
            var capture = await CaptureApplicationPixelsAsync(own, DesktopInterop.GetCursorSnapshot(), ct);
            using var windowImage = new SKBitmap(capture.Width, capture.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            Marshal.Copy(capture.Pixels, 0, windowImage.GetPixels(), capture.Pixels.Length);
            cropped = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(cropped);
            canvas.DrawBitmap(windowImage, new SKRectI(x, y, x + w, y + h), new SKRect(0, 0, w, h));
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacDesktop.DemandScreenCapture();
            var folder = Path.Combine(PortableStorage.Root, "Captures");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png");
            try
            {
                var start = new ProcessStartInfo("/usr/sbin/screencapture") { UseShellExecute = false, RedirectStandardError = true };
                foreach (var arg in new[] { "-x", "-o", "-t", "png", "-l", own.NativeId.ToString(), path }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start) ?? throw new IOException("Capture macOS indisponible.");
                try { await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(20), ct); }
                catch { if (!process.HasExited) process.Kill(true); throw; }
                if (process.ExitCode != 0) throw new IOException(await process.StandardError.ReadToEndAsync(ct));
                using var windowImage = SKBitmap.Decode(path) ?? throw new IOException("Capture macOS vide.");
                var sx = windowImage.Width / Math.Max(1, own.Width);
                var sy = windowImage.Height / Math.Max(1, own.Height);
                var leftInset = Math.Max(0, (own.Width - root.ActualWidth) / 2);
                var topInset = Math.Max(0, own.Height - root.ActualHeight);
                var x = (int)Math.Round((leftInset + local.X) * sx);
                var y = (int)Math.Round((topInset + local.Y) * sy);
                var w = Math.Max(1, (int)Math.Round(browser.ActualWidth * sx));
                var h = Math.Max(1, (int)Math.Round(browser.ActualHeight * sy));
                if (x < 0 || y < 0 || x + w > windowImage.Width || y + h > windowImage.Height)
                    throw new IOException("Zone du navigateur hors de la capture macOS.");
                cropped = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var canvas = new SKCanvas(cropped);
                canvas.DrawBitmap(windowImage, new SKRectI(x, y, x + w, y + h), new SKRect(0, 0, w, h));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        else throw new PlatformNotSupportedException("Embedded browser screenshot requires Windows or macOS.");

        using (cropped)
        using (var resized = cropped.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear))
            ?? throw new IOException("Capture du navigateur vide."))
        {
            if (browserPointerX.HasValue && browserPointerY.HasValue)
            {
                var pixels = new byte[width * height * 4];
                Marshal.Copy(resized.GetPixels(), pixels, 0, pixels.Length);
                DrawCursorGlyph(pixels, width, height, (int)Math.Round(browserPointerX.Value), (int)Math.Round(browserPointerY.Value));
                Marshal.Copy(pixels, 0, resized.GetPixels(), pixels.Length);
            }
            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            var bytes = encoded.ToArray();
            if (bytes.Length > 8 * 1024 * 1024) throw new IOException("Capture trop volumineuse (8 Mo maximum).");
            return bytes;
        }
    }
#endif
}
