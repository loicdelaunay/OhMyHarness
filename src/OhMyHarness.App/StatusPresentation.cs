using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    enum StatusKind { Activity, Notice, Error }
    readonly record struct StatusEntry(string Text, StatusKind Kind, DateTimeOffset? ExpiresAt);

    readonly DispatcherTimer statusPulseTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    Grid? statusGlowLayer;
    Border? statusChipView;
    StatusKind visibleStatusKind;
    DateTimeOffset visibleStatusStarted;
    DateTimeOffset? visibleStatusExpiresAt;
    int? visibleStatusChatId;
    bool IsTransientOverlayVisible => visibleStatusKind == StatusKind.Notice && visibleStatusChatId == null
        && visibleStatusExpiresAt is { } expiry && expiry > DateTimeOffset.UtcNow && !string.IsNullOrWhiteSpace(status.Text);

    static DateTimeOffset? StatusExpiry(string text, StatusKind kind) => kind == StatusKind.Notice
        ? DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(2 + text.Length / 20d, 4.5, 8)) : null;

    void ConnectStatusGlow(Border chip, Grid glow)
    {
        statusChipView = chip;
        statusGlowLayer = glow;
        glow.Opacity = 0;
        statusPulseTimer.Tick += (_, _) => AnimateStatus();
    }

    void ShowStatus(string text, StatusKind kind = StatusKind.Notice, int? chatId = null, DateTimeOffset? expiresAt = null)
    {
        if (string.IsNullOrWhiteSpace(text)) { ClearStatus(); return; }
        var now = DateTimeOffset.UtcNow;
        visibleStatusKind = kind;
        visibleStatusStarted = now;
        visibleStatusExpiresAt = expiresAt ?? StatusExpiry(text, kind);
        visibleStatusChatId = chatId;
        status.Text = text;
        if (statusChipView != null) statusChipView.Opacity = 1;
        if (statusGlowLayer != null) statusGlowLayer.Opacity = 0;
        statusPulseTimer.Start();
    }

    void ClearStatus()
    {
        statusPulseTimer.Stop();
        status.Text = "";
        visibleStatusExpiresAt = null;
        visibleStatusChatId = null;
        if (statusChipView != null) statusChipView.Opacity = 1;
        if (statusGlowLayer != null) statusGlowLayer.Opacity = 0;
    }

    void RestoreConversationStatus()
    {
        if (chat != null && conversationStatuses.TryGetValue(chat.Id, out var entry))
        {
            if (entry.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
                conversationStatuses.Remove(chat.Id);
            else { ShowStatus(entry.Text, entry.Kind, chat.Id, entry.ExpiresAt); return; }
        }
        ClearStatus();
    }

    void AnimateStatus()
    {
        if (string.IsNullOrWhiteSpace(status.Text)) { statusPulseTimer.Stop(); return; }
        var now = DateTimeOffset.UtcNow;
        var elapsed = Math.Max(0, (now - visibleStatusStarted).TotalSeconds);
        if (visibleStatusKind == StatusKind.Notice && visibleStatusExpiresAt is { } expiry)
        {
            var remaining = (expiry - now).TotalSeconds;
            if (remaining <= 0)
            {
                if (visibleStatusChatId is { } owner && conversationStatuses.TryGetValue(owner, out var entry)
                    && entry.ExpiresAt is { } savedExpiry && savedExpiry <= now)
                    conversationStatuses.Remove(owner);
                RestoreConversationStatus();
                return;
            }
            if (statusChipView != null) statusChipView.Opacity = Math.Clamp(remaining / .35, 0, 1);
        }
        if (statusGlowLayer != null)
            statusGlowLayer.Opacity = visibleStatusKind == StatusKind.Activity
                ? .18 + .82 * (.5 - .5 * Math.Cos(2 * Math.PI * elapsed / 1.65))
                : elapsed < 1.4 ? Math.Sin(Math.PI * elapsed / 1.4) : 0;
        if (visibleStatusKind == StatusKind.Error && elapsed >= 1.4) statusPulseTimer.Stop();
    }
}
