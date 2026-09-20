using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    McpSession CreateMcpSession() => new(async ct =>
    {
        await using var context = new HarnessDb();
        return await context.McpServers.AsNoTracking().ToListAsync(ct);
    }, (secret, _) => Task.FromResult(KeyVault.Decrypt(secret)),
        (scope, title, details, ct) => RequestAccessAsync(scope, title, details, title, ct));

    void SetPendingMcpImage(Attachment image)
    {
        pendingToolScreenshot = image.Data; pendingToolScreenshotLabel = image.Name;
        pendingToolScreenshotMime = image.Mime; pendingToolScreenshotWidth = pendingToolScreenshotHeight = 0;
    }
}
