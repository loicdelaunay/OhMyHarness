using System.Text;

namespace OhMyHarness.Core;

internal readonly record struct SourceText(string Content, Encoding Encoding, byte[] Preamble)
{
    internal static bool TryDecode(byte[] bytes, out SourceText text)
    {
        Encoding encoding;
        int offset;
        if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 254, 255 }))
        {
            encoding = new UTF32Encoding(true, true, true);
            offset = 4;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 255, 254, 0, 0 }))
        {
            encoding = new UTF32Encoding(false, true, true);
            offset = 4;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }))
        {
            encoding = new UTF8Encoding(true, true);
            offset = 3;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 255, 254 }))
        {
            encoding = new UnicodeEncoding(false, true, true);
            offset = 2;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 254, 255 }))
        {
            encoding = new UnicodeEncoding(true, true, true);
            offset = 2;
        }
        else
        {
            encoding = new UTF8Encoding(false, true);
            offset = 0;
        }

        try
        {
            var content = encoding.GetString(bytes, offset, bytes.Length - offset);
            if (content.Contains('\0')) { text = default; return false; }
            text = new SourceText(content, encoding, bytes[..offset]);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = default;
            return false;
        }
    }

    internal byte[] Encode(string content)
    {
        var body = Encoding.GetBytes(content);
        if (Preamble.Length == 0) return body;
        var bytes = new byte[Preamble.Length + body.Length];
        Preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, Preamble.Length);
        return bytes;
    }
}
