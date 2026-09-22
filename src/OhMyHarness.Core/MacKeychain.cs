using System.Runtime.InteropServices;
using System.Text;

namespace OhMyHarness.Core;

internal static class MacKeychain
{
    internal const string Prefix = "OMH-KEYCHAIN-2:";
    const string Security = "/System/Library/Frameworks/Security.framework/Security";
    [DllImport(Security)] static extern int SecKeychainAddGenericPassword(nint keychain, uint serviceLength, byte[] service,
        uint accountLength, byte[] account, uint passwordLength, byte[] password, out nint item);
    [DllImport(Security)] static extern int SecKeychainFindGenericPassword(nint keychain, uint serviceLength, byte[] service,
        uint accountLength, byte[] account, out uint length, out nint password, out nint item);
    [DllImport(Security)] static extern int SecKeychainItemFreeContent(nint attributes, nint data);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] static extern void CFRelease(nint value);
    static readonly byte[] Service = Encoding.UTF8.GetBytes("OhMyHarness");
    static void DemandMac() { if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Clé conservée dans le trousseau macOS. Ressaisissez-la sur ce système."); }
    internal static byte[] Store(string key)
    {
        DemandMac();
        var id = Guid.NewGuid().ToString("N"); var account = Encoding.UTF8.GetBytes(id); var bytes = Encoding.UTF8.GetBytes(key);
        var status = SecKeychainAddGenericPassword(0, (uint)Service.Length, Service, (uint)account.Length, account, (uint)bytes.Length, bytes, out var item);
        Array.Clear(bytes); if (item != 0) CFRelease(item);
        if (status != 0) throw new IOException("Trousseau macOS : " + status);
        return Encoding.UTF8.GetBytes(Prefix + id);
    }
    internal static string Read(byte[] reference)
    {
        DemandMac(); var account = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(reference)[Prefix.Length..]);
        var status = SecKeychainFindGenericPassword(0, (uint)Service.Length, Service, (uint)account.Length, account, out var length, out var data, out var item);
        if (status != 0) throw new IOException("Trousseau macOS : " + status);
        try { var bytes = new byte[length]; Marshal.Copy(data, bytes, 0, bytes.Length); var value = Encoding.UTF8.GetString(bytes); Array.Clear(bytes); return value; }
        finally { SecKeychainItemFreeContent(0, data); if(item != 0) CFRelease(item); }
    }
}
