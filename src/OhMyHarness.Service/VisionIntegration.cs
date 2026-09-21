using OhMyHarness.Core;

namespace OhMyHarness.Service;

public sealed partial class HarnessService
{
    VisionBridge VisionFor(ConversationSession run) => run.Vision ??= new(run, http, Decrypt, Approve);
}
