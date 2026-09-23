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
        var row = new Grid { ColumnSpacing = 6, MinHeight = 40 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var toggle = new CheckBox { Content = "Sandbox", IsChecked = selectedChat.SandboxEnabled };
        toggle.Click += async (_, _) => await Guard(async () => {
            selectedChat.SandboxEnabled = toggle.IsChecked == true;
            await db.SaveChangesAsync();
            ShowStatus(english ? "Sandbox setting applies to the next message." : "Mode sandbox appliqué au prochain envoi.");
        });
        var info = new Button { Padding = new Thickness(7, 4, 7, 4) };
        FluentDesign.IconButton(info, "\uE946", english ? "How sandbox works" : "Fonctionnement de la sandbox", false);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(info, english ? "How sandbox works" : "Fonctionnement de la sandbox");
        var explanation = new TextBlock { Text = english ? SandboxWorkspace.InfoEn : SandboxWorkspace.InfoFr, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
        info.Flyout = new Flyout { Content = explanation };
        ToolTipService.SetToolTip(info, english ? "How sandbox works" : "Comment fonctionne la sandbox");
        row.Children.Add(toggle); Grid.SetColumn(info, 1); row.Children.Add(info);
        var review = new Button { Content = english ? "Changes…" : "Modifications…", MinHeight = 34 };
        review.IsEnabled = !conversationRuns.ContainsKey(selectedChat.Id);
        ToolTipService.SetToolTip(review, english ? "Review sandbox changes before applying them" : "Examiner les modifications sandbox avant de les appliquer");
        review.Click += async (_, _) => await Guard(() => ReviewSandboxAsync(selectedChat));
        Grid.SetColumn(review, 2); row.Children.Add(review);
        content.Children.Add(row);
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
            ShowStatus(await sandbox.ApplyAsync(review, CancellationToken.None));
    }
}
