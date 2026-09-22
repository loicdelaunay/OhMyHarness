using System.Security.Cryptography;

namespace OhMyHarness.Core;

public static class BrandingAssets
{
    public const string DefaultName = "OhMyHarness";
    public static string DisplayName(string? value)
    {
        var name = string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return name.Length == 0 ? DefaultName : name[..Math.Min(name.Length, 80)];
    }
    public static string Resolve(string path, string? root = null)
    {
        root ??= PortableStorage.Root;
        if (string.IsNullOrWhiteSpace(path)) return "";
        return Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar), root);
    }
    public static string? RelativePath(string path, string? root = null)
    {
        root ??= PortableStorage.Root;
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) return null;
        return relative.Replace('\\', '/');
    }
    // Import external files only when settings are saved; never overwrite the user's source image.
    public static async Task<string> SaveLogoAsync(string path, byte[] data, string? root = null)
    {
        root ??= PortableStorage.Root;
        if (RelativePath(path, root) is { } relative) return relative;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".ico")) throw new ArgumentException("Format de logo non pris en charge.");
        var name = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() + extension;
        var folder = Path.Combine(root, "branding"); Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, name);
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(staging, data); File.Move(staging, destination, overwrite: true); }
        finally { if (File.Exists(staging)) File.Delete(staging); }
        return "branding/" + name;
    }
}
