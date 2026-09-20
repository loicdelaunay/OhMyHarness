using Markdig;
using Markdig.Syntax;

namespace OhMyHarness.Core;

public static class MarkdownPipelineHelper
{
    public static MarkdownPipeline Pipeline { get; } = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .Build();

    public static MarkdownDocument Parse(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return new MarkdownDocument();
        var document = Markdown.Parse(markdown, Pipeline);
        LocalFileLinks.Decorate(document);
        return document;
    }
}
