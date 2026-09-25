using System.Security.Cryptography;
using System.Text;

namespace OhMyHarness.Core;

/// <summary>Portable Linux secret protection. The adjacent installation key needs owner-only access.</summary>
internal static class LinuxKeyVault
{
    internal const string Prefix = "OMH-LINUX-1:";
    static string KeyPath => Path.Combine(PortableStorage.Root, ".linux-key");

    static byte[] ReadKey()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        PortableStorage.EnsureWritable();
        var path = KeyPath;
        SandboxWorkspace.AssertNoLinks(path);
        if (!File.Exists(path))
        {
            var generated = RandomNumberGenerator.GetBytes(32);
            try
            {
                using var stream = new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                });
                stream.Write(generated);
                stream.Flush(true);
            }
            catch (IOException) when (File.Exists(path)) { /* Another instance created the key. */ }
            finally { CryptographicOperations.ZeroMemory(generated); }
        }
        SandboxWorkspace.AssertNoLinks(path);
        var mode = File.GetUnixFileMode(path);
        if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                     UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new UnauthorizedAccessException("Linux key file must be accessible only to its owner: " + path);
        var key = File.ReadAllBytes(path);
        if (key.Length != 32) throw new InvalidDataException("Invalid Linux key file: " + path);
        return key;
    }

    internal static byte[] Encrypt(string text)
    {
        var key = ReadKey();
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var input = Encoding.UTF8.GetBytes(text);
            var ciphertext = new byte[input.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, input, ciphertext, tag);
            var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
            nonce.CopyTo(payload, 0);
            tag.CopyTo(payload, nonce.Length);
            ciphertext.CopyTo(payload, nonce.Length + tag.Length);
            return Encoding.UTF8.GetBytes(Prefix + Convert.ToBase64String(payload));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal static string Decrypt(byte[] data)
    {
        var encoded = Encoding.UTF8.GetString(data);
        if (!encoded.StartsWith(Prefix, StringComparison.Ordinal)) throw new InvalidDataException("Unsupported Linux key format.");
        var payload = Convert.FromBase64String(encoded[Prefix.Length..]);
        if (payload.Length < 28) throw new InvalidDataException("Truncated Linux key payload.");
        var key = ReadKey();
        try
        {
            var plaintext = new byte[payload.Length - 28];
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(28), payload.AsSpan(12, 16), plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
