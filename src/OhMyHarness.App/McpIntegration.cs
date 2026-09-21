using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    McpSession CreateMcpSession(int chatId) => new(async ct =>
    {
        await using var context = new HarnessDb();
        var list = await context.McpServers.AsNoTracking().ToListAsync(ct);
        var features = FeatureSettings.Read((await context.States.AsNoTracking().SingleAsync(ct)).FeaturesJson);
        if(features.BrowserMode == "chrome") list.Add(features.ChromeServer(chatId));
        return list;
    }, (secret, _) => Task.FromResult(KeyVault.Decrypt(secret)),
        (scope, title, details, ct) => RequestAccessAsync(scope, title, details, title, ct));

    void SetPendingMcpImage(Attachment image)
    {
        pendingToolScreenshot = image.Data; pendingToolScreenshotLabel = image.Name;
        pendingToolScreenshotMime = image.Mime; pendingToolScreenshotWidth = pendingToolScreenshotHeight = 0;
    }
}
