using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Text;
using static OhMyHarness.App.UiText;

using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace OhMyHarness.App;

public sealed class MarkdownRenderer
{
    private SolidColorBrush Brush(byte r, byte g, byte b) => new(ColorHelper.FromArgb(255, r, g, b));

    readonly Func<string, Task>? openFile;
    MarkdownRenderer(Func<string, Task>? openFile) { this.openFile = openFile; }
    public static void RenderTo(Panel container, string? markdown, Func<string, Task>? openFile = null) => new MarkdownRenderer(openFile).Render(container, markdown);
    void Render(Panel container, string? markdown)
    {
        container.Children.Clear();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var doc = MarkdownPipelineHelper.Parse(markdown);
        if (doc.Count == 0)
        {
            var fallback = new RichTextBlock
            {
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush(228, 233, 242),
                FontSize = 14.5
            };
            var p = new Paragraph();
            p.Inlines.Add(new Run { Text = markdown });
            fallback.Blocks.Add(p);
            container.Children.Add(fallback);
            return;
        }

        foreach (var block in doc)
        {
            var elem = RenderBlock(block);
            if (elem != null)
            {
                container.Children.Add(elem);
            }
        }
    }

    public FrameworkElement? RenderBlock(MdBlock block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                return RenderHeading(heading);

            case ParagraphBlock paragraph:
                return RenderParagraph(paragraph);

            case FencedCodeBlock fenced:
                return RenderFencedCode(fenced);

            case CodeBlock code:
                return RenderCodeBlock(code);

            case QuoteBlock quote:
                return RenderQuote(quote);

            case ListBlock list:
                return RenderList(list);

            case ThematicBreakBlock:
                return RenderThematicBreak();

            case Table table:
                return RenderTable(table);

            case ContainerBlock container:
                var panel = new StackPanel { Spacing = 4 };
                foreach (var child in container)
                {
                    var childElem = RenderBlock(child);
                    if (childElem != null) panel.Children.Add(childElem);
                }
                return panel;

            default:
                return null;
        }
    }

    private FrameworkElement RenderHeading(HeadingBlock heading)
    {
        var (fontSize, weight, margin) = heading.Level switch
        {
            1 => (21.0, FontWeights.Bold, new Thickness(0, 10, 0, 4)),
            2 => (18.0, FontWeights.SemiBold, new Thickness(0, 8, 0, 4)),
            3 => (16.0, FontWeights.SemiBold, new Thickness(0, 6, 0, 3)),
            4 => (14.5, FontWeights.SemiBold, new Thickness(0, 4, 0, 2)),
            _ => (13.5, FontWeights.SemiBold, new Thickness(0, 3, 0, 2))
        };

        var rtb = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(240, 245, 255),
            FontSize = fontSize,
            FontWeight = weight,
            Margin = margin
        };

        var p = new Paragraph();
        if (heading.Inline != null)
        {
            RenderInlines(heading.Inline, p.Inlines);
        }
        rtb.Blocks.Add(p);
        return rtb;
    }

    private FrameworkElement RenderParagraph(ParagraphBlock paragraph)
    {
        var rtb = new RichTextBlock
        {
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(224, 230, 240),
            FontSize = 14.5,
            LineHeight = 22,
            Margin = new Thickness(0, 2, 0, 4)
        };

        var p = new Paragraph();
        if (paragraph.Inline != null)
        {
            RenderInlines(paragraph.Inline, p.Inlines);
        }
        rtb.Blocks.Add(p);
        return rtb;
    }

    private FrameworkElement RenderFencedCode(FencedCodeBlock fenced)
    {
        var rawCode = ExtractCode(fenced.Lines);
        var lang = string.IsNullOrWhiteSpace(fenced.Info) ? "code" : fenced.Info.Trim();
        return CreateCodeBlockElement(lang, rawCode);
    }

    private FrameworkElement RenderCodeBlock(CodeBlock code)
    {
        var rawCode = ExtractCode(code.Lines);
        return CreateCodeBlockElement("code", rawCode);
    }

    private string ExtractCode(StringLineGroup lines)
    {
        return lines.ToString();
    }

    private FrameworkElement CreateCodeBlockElement(string language, string code)
    {
        var container = new Border
        {
            Background = Brush(16, 20, 28),
            BorderBrush = Brush(45, 54, 72),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 6, 0, 8)
        };

        var stack = new StackPanel();

        // Header bar with language and copy button
        var header = new Grid
        {
            Background = Brush(23, 28, 40),
            Padding = new Thickness(12, 5, 8, 5)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var langLabel = new TextBlock
        {
            Text = language.ToLowerInvariant(),
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            Foreground = Brush(145, 165, 195),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(langLabel, 0);
        header.Children.Add(langLabel);

        var copyBtn = new Button
        {
            Content = "📋 " + T("Copier"),
            FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3),
            Background = Brush(34, 40, 56),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Foreground = Brush(210, 220, 235)
        };
        copyBtn.Click += async (_, _) =>
        {
            try
            {
                var dp = new DataPackage();
                dp.SetText(code);
                Clipboard.SetContent(dp);
                copyBtn.Content = "✓ " + T("Copié !");
                await Task.Delay(2000);
                copyBtn.Content = "📋 " + T("Copier");
            }
            catch { }
        };
        Grid.SetColumn(copyBtn, 1);
        header.Children.Add(copyBtn);
        stack.Children.Add(header);

        // Code body
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(12, 10, 12, 10)
        };
        var codeText = new TextBlock
        {
            Text = code,
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 12.5,
            Foreground = Brush(220, 230, 245),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.NoWrap
        };
        scroll.Content = codeText;
        stack.Children.Add(scroll);

        container.Child = stack;
        return container;
    }

    private FrameworkElement RenderQuote(QuoteBlock quote)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = Brush(90, 140, 230),
            Background = Brush(21, 25, 36),
            CornerRadius = new CornerRadius(0, 6, 6, 0),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 4, 0, 6)
        };

        var stack = new StackPanel { Spacing = 4 };
        foreach (var child in quote)
        {
            var childElem = RenderBlock(child);
            if (childElem != null) stack.Children.Add(childElem);
        }
        border.Child = stack;
        return border;
    }

    private FrameworkElement RenderList(ListBlock list)
    {
        var stack = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 2, 0, 6)
        };

        int index = 1;
        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                string bulletText = list.IsOrdered ? $"{index}. " : "• ";
                var bullet = new TextBlock
                {
                    Text = bulletText,
                    Foreground = Brush(130, 175, 245),
                    FontSize = list.IsOrdered ? 13 : 15,
                    FontFamily = list.IsOrdered ? new FontFamily("Cascadia Code, Segoe UI") : new FontFamily("Segoe UI"),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(4, list.IsOrdered ? 1 : -1, 8, 0)
                };
                Grid.SetColumn(bullet, 0);
                rowGrid.Children.Add(bullet);

                var itemContent = new StackPanel { Spacing = 2 };
                foreach (var child in listItem)
                {
                    var childElem = RenderBlock(child);
                    if (childElem != null) itemContent.Children.Add(childElem);
                }
                Grid.SetColumn(itemContent, 1);
                rowGrid.Children.Add(itemContent);

                stack.Children.Add(rowGrid);
                index++;
            }
        }
        return stack;
    }

    private FrameworkElement RenderThematicBreak()
    {
        return new Border
        {
            Height = 1,
            Background = Brush(45, 55, 75),
            Margin = new Thickness(0, 8, 0, 8)
        };
    }

    private FrameworkElement RenderTable(Table table)
    {
        var container = new Border
        {
            Background = Brush(20, 24, 34),
            BorderBrush = Brush(45, 55, 75),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 6, 0, 8),
            Padding = new Thickness(4)
        };

        var grid = new Grid();
        int maxCols = 0;
        var rows = table.OfType<TableRow>().ToList();
        foreach (var row in rows)
        {
            var cellCount = row.OfType<TableCell>().Count();
            if (cellCount > maxCols) maxCols = cellCount;
        }

        for (int c = 0; c < maxCols; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (int r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = rows[r];
            var cells = row.OfType<TableCell>().ToList();
            bool isHeader = row.IsHeader;

            for (int c = 0; c < cells.Count && c < maxCols; c++)
            {
                var cell = cells[c];
                var cellBorder = new Border
                {
                    Background = isHeader ? Brush(30, 36, 52) : (r % 2 == 1 ? Brush(23, 27, 38) : Brush(19, 23, 32)),
                    Padding = new Thickness(8, 5, 8, 5),
                    Margin = new Thickness(1)
                };

                var cellContent = new StackPanel();
                foreach (var block in cell)
                {
                    var elem = RenderBlock(block);
                    if (elem != null) cellContent.Children.Add(elem);
                }
                cellBorder.Child = cellContent;

                Grid.SetRow(cellBorder, r);
                Grid.SetColumn(cellBorder, c);
                grid.Children.Add(cellBorder);
            }
        }

        container.Child = grid;
        return container;
    }

    private void RenderInlines(ContainerInline inlines, InlineCollection target)
    {
        foreach (var inline in inlines)
        {
            RenderInline(inline, target);
        }
    }

    private void RenderInline(MdInline inline, InlineCollection target)
    {
        switch (inline)
        {
            case LiteralInline literal:
                target.Add(new Run { Text = literal.Content.ToString() });
                break;

            case EmphasisInline emphasis:
                if (emphasis.DelimiterChar == '~')
                {
                    var span = new Span { TextDecorations = TextDecorations.Strikethrough };
                    RenderInlines(emphasis, span.Inlines);
                    target.Add(span);
                }
                else if (emphasis.DelimiterCount >= 2)
                {
                    var bold = new Bold();
                    RenderInlines(emphasis, bold.Inlines);
                    target.Add(bold);
                }
                else
                {
                    var italic = new Italic();
                    RenderInlines(emphasis, italic.Inlines);
                    target.Add(italic);
                }
                break;

            case CodeInline code:
                target.Add(new Run
                {
                    Text = code.Content,
                    FontFamily = new FontFamily("Cascadia Code, Consolas"),
                    Foreground = Brush(240, 195, 120),
                    FontWeight = FontWeights.Medium
                });
                break;

            case LineBreakInline:
                target.Add(new LineBreak());
                break;

            case LinkInline link:
                if (link.IsImage)
                {
                    target.Add(new Run
                    {
                        Text = $"[{(string.IsNullOrEmpty(link.Title) ? "Image" : link.Title)}]",
                        Foreground = Brush(145, 160, 185)
                    });
                }
                else
                {
                    var hyperlink = new Hyperlink
                    {
                        Foreground = Brush(120, 175, 255),
                        UnderlineStyle = UnderlineStyle.Single
                    };
                    if (LocalFileLinks.PathFromUrl(link.Url) is string path && openFile != null)
                    {
                        hyperlink.Click += async (_, _) => await openFile(path);
                    }
                    else if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto")
                    {
                        hyperlink.Click += async (_, _) =>
                        {
                            try { await Launcher.LaunchUriAsync(uri); } catch { }
                        };
                    }
                    RenderInlines(link, hyperlink.Inlines);
                    target.Add(hyperlink);
                }
                break;

            case AutolinkInline autolink:
                var autoHyperlink = new Hyperlink
                {
                    Foreground = Brush(120, 175, 255),
                    UnderlineStyle = UnderlineStyle.Single
                };
                if (Uri.TryCreate(autolink.Url, UriKind.Absolute, out var autoUri))
                {
                    autoHyperlink.Click += async (_, _) =>
                    {
                        try { await Launcher.LaunchUriAsync(autoUri); } catch { }
                    };
                }
                autoHyperlink.Inlines.Add(new Run { Text = autolink.Url });
                target.Add(autoHyperlink);
                break;

            case TaskList task:
                target.Add(new Run
                {
                    Text = task.Checked ? "☑ " : "☐ ",
                    Foreground = task.Checked ? Brush(100, 220, 140) : Brush(160, 170, 190),
                    FontWeight = FontWeights.SemiBold
                });
                break;

            case HtmlInline html:
                target.Add(new Run { Text = html.Tag });
                break;

            case ContainerInline container:
                RenderInlines(container, target);
                break;

            default:
                target.Add(new Run { Text = inline.ToString() });
                break;
        }
    }
}
