using OhMyHarness.Core;

namespace OhMyHarness.Core.Hosting;

public sealed partial class HarnessService
{
    VisionBridge VisionFor(ConversationSession run) => run.Vision ??= new(run, http, Decrypt, Approve);
}
