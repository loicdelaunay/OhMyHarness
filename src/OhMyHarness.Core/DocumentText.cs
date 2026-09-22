using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace OhMyHarness.Core;

public static class DocumentText
{
    public const int MaxBytes = 32 * 1024 * 1024, MaxCharacters = 500000;
    public static async Task<string> FingerprintAsync(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) return $"metadata:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
    public static async Task<string> ReadAsync(string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        var metadata = $"Fichier : {info.Name}\nType : {info.Extension}\nTaille : {info.Length} octets\n";
        if (info.Length > MaxBytes) return metadata + "[Contenu non extrait : limite de 32 Mio. Métadonnées seulement.]";
        var bytes = await File.ReadAllBytesAsync(path, ct); string text;
        try
        {
            if (bytes.AsSpan().StartsWith("%PDF"u8))
            {
                using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
                var output = new StringBuilder();
                foreach (var page in pdf.GetPages()) { ct.ThrowIfCancellationRequested(); output.AppendLine($"[Page {page.Number}]\n{page.Text}"); if (output.Length >= MaxCharacters) break; }
                text = output.ToString();
                if (string.IsNullOrWhiteSpace(string.Join("", pdf.GetPages().Take(1).Select(x => x.Text)))) text += "\n[PDF potentiellement scanné : OCR non disponible dans l’extracteur texte.]";
            }
            else if (bytes.AsSpan().StartsWith("PK\u0003\u0004"u8))
            {
                using var zip = new ZipArchive(new MemoryStream(bytes)); var output = new StringBuilder();
                foreach (var entry in zip.Entries.Take(2000))
                {
                    ct.ThrowIfCancellationRequested();
                    bool document = entry.FullName.StartsWith("word/") || entry.FullName.StartsWith("ppt/slides/") || entry.FullName.StartsWith("xl/") || entry.FullName == "content.xml" || entry.FullName.EndsWith(".xhtml");
                    if (!document || entry.Length > 4_000_000) { output.AppendLine($"[Archive] {entry.FullName} ({entry.Length} octets)"); }
                    else
                    {
                        using var content = entry.Open();
                        using var reader = XmlReader.Create(content, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4_000_000 });
                        try { var xml = XDocument.Load(reader); output.AppendLine($"[{entry.FullName}]\n" + string.Join(" ", xml.DescendantNodes().OfType<XText>().Select(x => x.Value))); }
                        catch (XmlException) { output.AppendLine("[Entrée XML non lisible] " + entry.FullName); }
                    }
                    if (output.Length >= MaxCharacters) break;
                }
                text = output.ToString();
            }
            else
            {
                var encoding = bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }) ? Encoding.Unicode : bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }) ? Encoding.BigEndianUnicode : null;
                if (encoding != null) text = encoding.GetString(bytes).TrimStart('\ufeff');
                else
                {
                    try { text = new UTF8Encoding(false, true).GetString(bytes); }
                    catch (DecoderFallbackException) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); text = Encoding.GetEncoding(1252).GetString(bytes); }
                    if (text.Contains('\0') || text.Count(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) > Math.Max(4, text.Length / 100))
                        text = metadata + "[Format binaire : extraction partielle de chaînes lisibles, pas une interprétation du fichier.]\n" + string.Join('\n', Regex.Matches(Encoding.Latin1.GetString(bytes), @"[ -~]{6,}", RegexOptions.None, TimeSpan.FromSeconds(2)).Take(300).Select(m => m.Value[..Math.Min(500, m.Length)]));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { text = metadata + "[Extraction indisponible : " + ex.GetType().Name + ". Métadonnées seulement.]"; }
        return text.Length <= MaxCharacters ? text : text[..MaxCharacters] + "\n[Extraction tronquée à 500 000 caractères]";
    }
    public static string Lines(string text, int start, int end)
    {
        if (start < 1 || end < start || end - start >= 2000) throw new ArgumentException("Plage 1-based, 2000 lignes maximum.");
        return string.Join('\n', text.Replace("\r\n", "\n").Split('\n').Skip(start - 1).Take(end - start + 1).Select((s,i) => $"{start+i}: {s}"));
    }
}
