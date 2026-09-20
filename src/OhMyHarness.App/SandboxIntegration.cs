using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    void AddSandboxMenu(StackPanel content, Chat selectedChat)
    {
        bool english = state.Language == "en";
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var toggle = new CheckBox { Content = "Sandbox", IsChecked = selectedChat.SandboxEnabled };
        toggle.Click += async (_, _) => await Guard(async () => {
            selectedChat.SandboxEnabled = toggle.IsChecked == true;
            await db.SaveChangesAsync();
            status.Text = english ? "Sandbox setting applies to the next message." : "Mode sandbox appliqué au prochain envoi.";
        });
        var info = new Button { Content = "ⓘ", Padding = new Thickness(8, 4, 8, 4) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(info, english ? "How sandbox works" : "Fonctionnement de la sandbox");
        var explanation = new TextBlock { Text = english ? SandboxWorkspace.InfoEn : SandboxWorkspace.InfoFr, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
        info.Flyout = new Flyout { Content = explanation };
        ToolTipService.SetToolTip(info, english ? "How sandbox works" : "Comment fonctionne la sandbox");
        row.Children.Add(toggle); row.Children.Add(info); content.Children.Add(row);
        var review = new Button { Content = english ? "Review sandbox changes…" : "Examiner les modifications sandbox…", HorizontalAlignment = HorizontalAlignment.Stretch };
        review.IsEnabled = !conversationRuns.ContainsKey(selectedChat.Id);
        review.Click += async (_, _) => await Guard(() => ReviewSandboxAsync(selectedChat));
        content.Children.Add(review);
    }
    async Task ReviewSandboxAsync(Chat selectedChat)
    {
        if (conversationRuns.ContainsKey(selectedChat.Id)) throw new InvalidOperationException("Attendez la fin de la conversation / Wait for the conversation to finish.");
        var selectedProject = db.Projects.Local.First(x => x.Id == selectedChat.ProjectId);
        using var sandbox = await SandboxWorkspace.OpenAsync(HarnessDb.DatabasePath, selectedChat.Id, selectedProject.GetSourceFolders(), CancellationToken.None);
        var review = await sandbox.ReviewAsync(CancellationToken.None);
        bool english = state.Language == "en";
        var diff = new TextBox { Text = review.Diff, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"), MaxHeight = 480, MinWidth = 400 };
        ScrollViewer.SetVerticalScrollBarVisibility(diff, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(diff, ScrollBarVisibility.Auto);
        var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = english ? "Sandbox → original project" : "Sandbox → projet réel", Content = diff,
            PrimaryButtonText = review.Count > 0 ? (english ? "Apply these changes" : "Appliquer ces modifications") : "",
            CloseButtonText = english ? "Close" : "Fermer", DefaultButton = ContentDialogButton.Close };
        // Explicit review is independent of the global auto-approval/remembered-grant policy.
        if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary)
            status.Text = await sandbox.ApplyAsync(review, CancellationToken.None);
    }
}
