using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SkiaSharp;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace OhMyHarness.App;

internal static class ClipboardImages
{
    const int MaxRawBytes = 64 * 1024 * 1024;
    const int MaxAttachmentBytes = 8 * 1024 * 1024;

    internal static byte[]? ReadNative() => OperatingSystem.IsWindows() ? WindowsClipboard.Read()
        : OperatingSystem.IsMacOS() ? MacClipboard.Read() : null;

    internal static bool HasWinRtBitmap(DataPackageView? view)
    {
#if WINDOWS
        return view?.Contains(StandardDataFormats.Bitmap) == true;
#else
        return false;
#endif
    }

    internal static async Task<byte[]> ReadWinRtBitmapAsync(DataPackageView view)
    {
#if WINDOWS
        var reference = await view.GetBitmapAsync();
        using var stream = await reference.OpenReadAsync();
        if (stream.Size is 0 or > MaxRawBytes) throw new IOException("Image du presse-papiers vide ou trop volumineuse.");
        using var reader = new DataReader(stream);
        if (await reader.LoadAsync((uint)stream.Size) != stream.Size)
            throw new IOException("Lecture incomplète de l’image du presse-papiers.");
        var bytes = new byte[(int)stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException();
#endif
    }

    internal static byte[] NormalizePng(byte[] encoded)
    {
        if (encoded.Length is 0 or > MaxRawBytes) throw new IOException("Image du presse-papiers vide ou trop volumineuse.");
        using var source = new SKMemoryStream(encoded);
        using var codec = SKCodec.Create(source);
        if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
            codec.Info.Width > 16384 || codec.Info.Height > 16384 ||
            (long)codec.Info.Width * codec.Info.Height > 40_000_000)
            throw new IOException("Image du presse-papiers invalide ou trop grande.");
        using var original = SKBitmap.Decode(encoded) ?? throw new IOException("Décodage de l’image du presse-papiers impossible.");
        SKBitmap current = original;
        try
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                using var image = SKImage.FromBitmap(current);
                using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                if (png.Size <= MaxAttachmentBytes) return png.ToArray();
                var width = Math.Max(1, current.Width * 3 / 4);
                var height = Math.Max(1, current.Height * 3 / 4);
                if (width == current.Width && height == current.Height) break;
                var next = current.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear));
                if (!ReferenceEquals(current, original)) current.Dispose();
                current = next ?? throw new IOException("Redimensionnement de l’image impossible.");
            }
            throw new IOException("Image du presse-papiers trop volumineuse (8 Mo maximum).");
        }
        finally { if (!ReferenceEquals(current, original)) current.Dispose(); }
    }

    internal static bool MacPasteShortcutDown() => OperatingSystem.IsMacOS() && (MacClipboard.ModifierFlags() & (1UL << 20)) != 0;

    static class WindowsClipboard
    {
        const uint CfDib = 8, CfDibV5 = 17;
        [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(nint owner);
        [DllImport("user32.dll")] static extern bool CloseClipboard();
        [DllImport("user32.dll")] static extern nint GetClipboardData(uint format);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string format);
        [DllImport("kernel32.dll")] static extern nint GlobalLock(nint handle);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(nint handle);
        [DllImport("kernel32.dll")] static extern nuint GlobalSize(nint handle);

        internal static byte[]? Read()
        {
            if (!OpenClipboard(0)) return null;
            try
            {
                var png = Copy(RegisterClipboardFormat("PNG"));
                if (png != null) return png;
                var dib = Copy(CfDibV5) ?? Copy(CfDib);
                return dib == null ? null : WrapDib(dib);
            }
            finally { CloseClipboard(); }
        }

        static byte[]? Copy(uint format)
        {
            if (format == 0) return null;
            var handle = GetClipboardData(format);
            if (handle == 0) return null;
            var size = GlobalSize(handle);
            if (size is 0 or > MaxRawBytes) return null;
            var pointer = GlobalLock(handle);
            if (pointer == 0) return null;
            try
            {
                var bytes = new byte[(int)size];
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { GlobalUnlock(handle); }
        }

        internal static byte[]? WrapDib(byte[] dib)
        {
            if (dib.Length < 40) return null;
            var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
            if (headerSize < 40 || headerSize > dib.Length) return null;
            var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
            var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16));
            var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(32));
            var masks = headerSize == 40 && compression is 3 or 6 ? compression == 6 ? 16 : 12 : 0;
            var colors = bitsPerPixel <= 8 ? colorsUsed == 0 ? 1u << bitsPerPixel : colorsUsed : 0;
            var pixelOffset = 14L + headerSize + masks + colors * 4L;
            if (pixelOffset > dib.Length + 14L || pixelOffset > uint.MaxValue || dib.Length > int.MaxValue - 14) return null;
            var bmp = new byte[dib.Length + 14];
            bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
            BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), (uint)bmp.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), (uint)pixelOffset);
            dib.CopyTo(bmp, 14);
            return bmp;
        }
    }

    static class MacClipboard
    {
        const string ObjC = "/usr/lib/libobjc.A.dylib";
        const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        static readonly Lazy<nint> AppKit = new(() => NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"));
        [DllImport(ObjC)] static extern nint objc_getClass(string name);
        [DllImport(ObjC)] static extern nint sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(nint target, nint selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint SendPointer(nint target, nint selector, nint argument);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint SendString(nint target, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint SendImageType(nint target, nint selector, nint type, nint properties);
        [DllImport(CoreGraphics)] static extern ulong CGEventSourceFlagsState(uint stateId);

        internal static ulong ModifierFlags() => CGEventSourceFlagsState(0);

        internal static byte[]? Read()
        {
            _ = AppKit.Value;
            var pasteboard = Send(objc_getClass("NSPasteboard"), sel_registerName("generalPasteboard"));
            if (pasteboard == 0) return null;
            var png = DataForType(pasteboard, "public.png");
            if (png != 0) return CopyData(png);
            var tiff = DataForType(pasteboard, "public.tiff");
            if (tiff == 0) return null;
            var imageRepClass = objc_getClass("NSBitmapImageRep");
            var imageRep = SendPointer(Send(imageRepClass, sel_registerName("alloc")), sel_registerName("initWithData:"), tiff);
            if (imageRep == 0) return null;
            try
            {
                var properties = Send(objc_getClass("NSDictionary"), sel_registerName("dictionary"));
                var converted = SendImageType(imageRep, sel_registerName("representationUsingType:properties:"), 4, properties);
                return converted == 0 ? null : CopyData(converted);
            }
            finally { Send(imageRep, sel_registerName("release")); }
        }

        static nint DataForType(nint pasteboard, string type)
        {
            var nsType = SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), type);
            return SendPointer(pasteboard, sel_registerName("dataForType:"), nsType);
        }

        static byte[]? CopyData(nint data)
        {
            var length = Send(data, sel_registerName("length")).ToInt64();
            if (length is <= 0 or > MaxRawBytes) return null;
            var pointer = Send(data, sel_registerName("bytes"));
            if (pointer == 0) return null;
            var bytes = new byte[(int)length];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
