using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    void BuildGitTree(List<GitChangedFile> files)
    {
        foreach (var repo in files.GroupBy(x => x.Repository))
        {
            var rootNode = new TreeViewNode { Content = "📁 " + System.IO.Path.GetFileName(repo.Key), IsExpanded = true };
            gitFiles.RootNodes.Add(rootNode);
            var folders = new Dictionary<string, TreeViewNode> { [""] = rootNode };
            foreach (var file in repo.OrderBy(x => x.Path, StringComparer.Ordinal))
            {
                var parts = file.Path.Split('/'); var parent = rootNode; var path = "";
                foreach (var part in parts.SkipLast(1))
                {
                    path += part + "/";
                    if (!folders.TryGetValue(path, out var node))
                    {
                        node = new TreeViewNode { Content = "📁 " + part, IsExpanded = true };
                        folders[path] = node; parent.Children.Add(node);
                    }
                    parent = node;
                }
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Tag = file };
                row.Children.Add(new TextBlock { Text = parts[^1] + "  " + file.Status.Trim() });
                row.Children.Add(new TextBlock { Text = file.Added.HasValue ? $"+{file.Added}" : "—", Foreground = Brush(110, 220, 150) });
                row.Children.Add(new TextBlock { Text = file.Removed.HasValue ? $"−{file.Removed}" : "", Foreground = Brush(255, 145, 145) });
                ToolTipService.SetToolTip(row, file.Path + (file.PreviousPath == null ? "" : " ← " + file.PreviousPath) + "\n" + file.PreviewNotice);
                parent.Children.Add(new TreeViewNode { Content = row });
            }
        }
    }

    void RenderGitPreview(GitChangedFile file, GitWorkspace.DiffPreview preview)
    {
        gitDiff.Children.Add(new TextBlock { Text = file.Path, IsTextSelectionEnabled = true, Margin = new(0, 8, 0, 8) });
        var table = new Grid { ColumnSpacing = 12, MinWidth = 560 };
        table.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        void Add(string? left, string? right, string kind)
        {
            int index = table.RowDefinitions.Count;
            table.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (int column = 0; column < 2; column++)
            {
                var text = new TextBlock { Text = (column == 0 ? left : right) ?? "", Padding = new(6, 2, 6, 2),
                    FontFamily = new FontFamily("Cascadia Code, Consolas"), FontSize = 12, IsTextSelectionEnabled = true,
                    Foreground = kind == "removed" && column == 0 ? Brush(255, 145, 145) : kind == "added" && column == 1 ? Brush(110, 220, 150) : FluentDesign.Primary };
                var cell = new Border { Child = text, Background = kind == "context" ? null : FluentDesign.Card };
                Grid.SetColumn(cell, column); Grid.SetRow(cell, index); table.Children.Add(cell);
            }
        }
        Add(state.Language == "en" ? "Before · HEAD" : "Avant · HEAD", state.Language == "en" ? "After · Working tree" : "Après · Dossier de travail", "header");
        foreach (var row in preview.Rows)
            Add(row.Before == null ? null : $"{row.BeforeLine,5} {row.Before}", row.After == null ? null : $"{row.AfterLine,5} {row.After}", row.Kind);
        gitDiff.Children.Add(table);
        if (preview.Notice != null) gitDiff.Children.Add(new TextBlock { Text = preview.Notice, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
    }
}
