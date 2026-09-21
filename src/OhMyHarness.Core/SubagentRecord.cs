namespace OhMyHarness.Core;

public sealed class SubagentRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int ChatId { get; set; }
    public string Name { get; set; } = "";
    public string Task { get; set; } = "";
    public string Status { get; set; } = "running";
    public string Activity { get; set; } = "";
    public string TranscriptJson { get; set; } = "[]";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
