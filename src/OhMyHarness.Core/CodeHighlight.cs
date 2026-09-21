using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OhMyHarness.Core;

public static class CodeHighlight
{
    public record Token(string Text, string Color);
    static readonly HashSet<string> Keywords = new("abstract async await as assert break case catch class const continue def default delete do else elif enum export extends false final finally fn for foreach from function if implements import in interface internal is let namespace new none null override package pass private protected public readonly return self static struct super switch this throw true try type typeof using var void while with yield".Split(' '), StringComparer.OrdinalIgnoreCase);
    public static IEnumerable<Token> Tokens(string code, string language)
    {
        if (code.Length > 100000) { yield return new(code, "#dce6f5"); yield break; }
        bool hash = new[] { "python", "py", "ruby", "rb", "sh", "bash", "zsh", "powershell", "ps1", "yaml", "yml", "toml", "gdscript", "gd" }.Contains(language.ToLowerInvariant());
        var pattern = @"(?<comment>//[^\r\n]*|/\*[\s\S]*?\*/|<!--[\s\S]*?-->)|(?<str>""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`)|(?<number>\b\d+(?:\.\d+)?\b)|(?<word>\b[A-Za-z_$][\w$]*\b)";
        if (hash) pattern = @"(?<comment>\#[^\r\n]*)|" + pattern;
        int offset = 0;
        var matches = Regex.Matches(code, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
        foreach (Match m in matches)
        {
            if (m.Index > offset) yield return new(code[offset..m.Index], "#dce6f5");
            var color = m.Groups["comment"].Success ? "#7fa77b" : m.Groups["str"].Success ? "#ce9178" : m.Groups["number"].Success ? "#b5cea8" : Keywords.Contains(m.Value) ? "#c586c0" : "#9cdcfe";
            yield return new(m.Value, color); offset = m.Index + m.Length;
        }
        if (offset < code.Length) yield return new(code[offset..], "#dce6f5");
    }
    public static string Html(string html) => Regex.Replace(html, "<pre><code(?: class=\"language-([^\"]*)\")?>([\\s\\S]*?)</code></pre>", m =>
    {
        var code = WebUtility.HtmlDecode(m.Groups[2].Value);
        try { return "<pre><code>" + string.Concat(Tokens(code, m.Groups[1].Value).Select(t => $"<span class=\"code-{t.Color[1..]}\">{WebUtility.HtmlEncode(t.Text)}</span>")) + "</code></pre>"; }
        catch (RegexMatchTimeoutException) { return m.Value; }
    }, RegexOptions.None, TimeSpan.FromMilliseconds(300));
}
