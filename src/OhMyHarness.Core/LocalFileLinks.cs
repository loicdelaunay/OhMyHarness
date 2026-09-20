using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text.RegularExpressions;

namespace OhMyHarness.Core;

public static class LocalFileLinks
{
    public const string Prefix = "omh-file:";
    static readonly Regex Paths = new(@"(?<![\w:/\\])(?:[A-Za-z]:[/\\]|\.{1,2}[/\\]|/)?(?:[\w.@-]+[/\\])*[\w@-][\w.@-]*\.(?:html?|pdf|md|txt|json|csv|cs|ts|tsx|js|jsx|py|css|xaml|xml|yaml|yml|sql|svg|png|jpe?g|webp)(?![\w.])", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    public static string? PathFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith(Prefix, StringComparison.Ordinal)) return Uri.UnescapeDataString(url[Prefix.Length..]);
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.IsFile ? uri.LocalPath : null;
        if (url.StartsWith('#') || url.StartsWith("//")) return null;
        return Paths.IsMatch(url) ? Uri.UnescapeDataString(url) : null;
    }
    public static void Decorate(MarkdownDocument document)
    {
        void Inlines(ContainerInline container)
        {
            for (var child = container.FirstChild; child is not null;)
            {
                var next = child.NextSibling;
                if (child is LinkInline link)
                {
                    if (!link.IsImage && PathFromUrl(link.Url) is string path) link.Url = Prefix + Uri.EscapeDataString(path);
                }
                else if (child is ContainerInline nested) Inlines(nested);
                else if (child is LiteralInline or CodeInline)
                {
                    var text = child is LiteralInline literal ? literal.Content.ToString() : ((CodeInline)child).Content;
                    var matches = Paths.Matches(text); int offset = 0;
                    foreach (Match match in matches)
                    {
                        // Never turn fragments of a URL into local-file links.
                        var tokenStart = text.LastIndexOfAny([' ', '\t', '\n'], Math.Max(0, match.Index - 1));
                        var preceding = text[(tokenStart + 1)..match.Index];
                        if (preceding.Contains("://") || preceding.Contains('@')) continue;
                        if (match.Index > offset) child.InsertBefore(new LiteralInline(text[offset..match.Index]));
                        var fileLink = new LinkInline(Prefix + Uri.EscapeDataString(match.Value), "");
                        fileLink.AppendChild(child is CodeInline ? new CodeInline(match.Value) : new LiteralInline(match.Value));
                        child.InsertBefore(fileLink); offset = match.Index + match.Length;
                    }
                    if (offset > 0) { if (offset < text.Length) child.InsertBefore(new LiteralInline(text[offset..])); child.Remove(); }
                }
                child = next;
            }
        }
        void Blocks(ContainerBlock blocks)
        {
            foreach (var block in blocks)
            {
                if (block is LeafBlock { Inline: { } inline }) Inlines(inline);
                if (block is ContainerBlock nested) Blocks(nested);
            }
        }
        Blocks(document);
    }
}
