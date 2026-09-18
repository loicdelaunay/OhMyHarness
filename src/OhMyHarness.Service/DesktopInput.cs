using OhMyHarness.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace OhMyHarness.Service;

// Native calls are isolated here: no Windows DLL is loaded on macOS, or vice versa.
static class DesktopInput
{
    public static long Foreground() => OperatingSystem.IsMacOS() ? Mac.Foreground() : OperatingSystem.IsWindows() ? Win.GetForegroundWindow().ToInt64() : 0;
    public static void Restore(long target)
    {
        if (target == 0) return;
        if (OperatingSystem.IsMacOS()) Mac.Restore((int)target);
        else if (OperatingSystem.IsWindows()) Win.SetForegroundWindow((nint)target);
    }
    public static void Execute(string tool, JsonObject args)
    {
        var action = args["action"]?.GetValue<string>() ?? (tool == "desktop_mouse" ? "click" : "press");
        if (tool == "desktop_keyboard")
        {
            if (action == "type")
            {
                var text = args["text"]?.GetValue<string>() ?? "";
                if (text.Length is 0 or > 10000) throw new ArgumentException("Text must contain 1..10000 characters.");
                if (OperatingSystem.IsMacOS()) Mac.Type(text); else if (OperatingSystem.IsWindows()) Win.Type(text); else throw new PlatformNotSupportedException();
            }
            else if (action == "press")
            {
                var chord = KeyboardInput.ParseChord(args["keys"]?.GetValue<string>());
                if (OperatingSystem.IsMacOS()) Mac.Press(chord); else if (OperatingSystem.IsWindows()) Win.Press(chord); else throw new PlatformNotSupportedException();
            }
            else throw new ArgumentException("Keyboard action must be type or press.");
            return;
        }
        if (action is not ("click" or "move" or "scroll")) throw new ArgumentException("Mouse action must be move, click or scroll.");
        var x = args["x"]?.GetValue<double>() ?? throw new ArgumentException("x required");
        var y = args["y"]?.GetValue<double>() ?? throw new ArgumentException("y required");
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 100000 || Math.Abs(y) > 100000) throw new ArgumentException("Invalid mouse coordinates.");
        var right = MouseInput.NormalizeButton(args["button"]?.GetValue<string>()) == "right";
        int clicks = MouseInput.NormalizeClickCount(args["click_count"]?.GetValue<int>() ?? 1);
        var delta = Math.Clamp(args["delta_y"]?.GetValue<double>() ?? 0, -100000, 100000);
        if (OperatingSystem.IsMacOS()) Mac.Mouse(action, x, y, right, clicks, (int)delta);
        else if (OperatingSystem.IsWindows()) Win.Mouse(action, (int)x, (int)y, right, clicks, (int)delta);
        else throw new PlatformNotSupportedException();
    }

    static class Mac
    {
        const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        const string AX = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
        const string ObjC = "/usr/lib/libobjc.A.dylib";
        const string HIToolbox = "/System/Library/Frameworks/Carbon.framework/Frameworks/HIToolbox.framework/HIToolbox";
        [StructLayout(LayoutKind.Sequential)] struct Point(double x, double y) { public double X = x, Y = y; }
        [DllImport(CG)] static extern nint CGEventCreateKeyboardEvent(nint source, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
        [DllImport(CG)] static extern void CGEventKeyboardSetUnicodeString(nint ev, nuint length, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] text);
        [DllImport(CG)] static extern void CGEventSetFlags(nint ev, ulong flags);
        [DllImport(CG)] static extern nint CGEventCreateMouseEvent(nint source, uint type, Point point, uint button);
        [DllImport(CG)] static extern void CGEventSetIntegerValueField(nint ev, uint field, long value);
        [DllImport(CG)] static extern nint CGEventCreateScrollWheelEvent(nint source, uint units, uint count, int delta);
        [DllImport(CG)] static extern void CGEventPost(uint tap, nint ev);
        [DllImport(CF)] static extern void CFRelease(nint value);
        [DllImport(CF)] static extern nint CFDataGetBytePtr(nint value);
        [DllImport(HIToolbox)] static extern nint TISCopyCurrentKeyboardLayoutInputSource();
        [DllImport(HIToolbox)] static extern nint TISGetInputSourceProperty(nint source, nint key);
        [DllImport(HIToolbox)] static extern byte LMGetKbdType();
        [DllImport(HIToolbox)] static extern int UCKeyTranslate(nint layout, ushort code, ushort action, uint modifiers,
            uint keyboardType, uint options, ref uint deadKey, nuint capacity, out nuint length, [Out] ushort[] characters);
        static readonly Lazy<nint> LayoutProperty = new(() => Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(HIToolbox), "kTISPropertyUnicodeKeyLayoutData")));
        [DllImport(AX)] [return: MarshalAs(UnmanagedType.I1)] static extern bool AXIsProcessTrusted();
        [DllImport(ObjC)] static extern nint objc_getClass(string name);
        [DllImport(ObjC)] static extern nint sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(nint obj, nint selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint SendPid(nint obj, nint selector, int pid);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] static extern bool Activate(nint obj, nint selector, ulong flags);
        static readonly Lazy<nint> AppKit = new(() => NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"));
        public static long Foreground()
        {
            _ = AppKit.Value;
            var workspace = Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            var app = Send(workspace, sel_registerName("frontmostApplication"));
            return app == 0 ? 0 : Send(app, sel_registerName("processIdentifier")).ToInt64();
        }
        public static void Restore(int pid)
        {
            _ = AppKit.Value;
            var app = SendPid(objc_getClass("NSRunningApplication"), sel_registerName("runningApplicationWithProcessIdentifier:"), pid);
            if (app != 0) Activate(app, sel_registerName("activateWithOptions:"), 2);
        }
        static void Access()
        {
            if (!AXIsProcessTrusted()) throw new UnauthorizedAccessException("macOS : autorisez OhMyHarness dans Réglages Système > Confidentialité et sécurité > Accessibilité, puis relancez l’application. Application approval does not grant macOS Accessibility permission.");
        }
        static void Post(nint ev, Action<nint>? configure = null)
        {
            if (ev == 0) throw new IOException("Could not create a macOS input event.");
            try { configure?.Invoke(ev); CGEventPost(0, ev); } finally { CFRelease(ev); }
        }
        // Apple virtual key codes, including the native Command and Option keys.
        static readonly Dictionary<string, ushort> Codes = new()
        {
            ["A"]=0,["S"]=1,["D"]=2,["F"]=3,["H"]=4,["G"]=5,["Z"]=6,["X"]=7,["C"]=8,["V"]=9,["B"]=11,
            ["Q"]=12,["W"]=13,["E"]=14,["R"]=15,["Y"]=16,["T"]=17,["1"]=18,["2"]=19,["3"]=20,["4"]=21,["6"]=22,["5"]=23,
            ["PLUS"]=24,["9"]=25,["7"]=26,["MINUS"]=27,["8"]=28,["0"]=29,["O"]=31,["U"]=32,["I"]=34,["P"]=35,
            ["ENTER"]=36,["L"]=37,["J"]=38,["K"]=40,["N"]=45,["M"]=46,["TAB"]=48,["SPACE"]=49,["BACKSPACE"]=51,
            ["ESCAPE"]=53,["WIN"]=55,["SHIFT"]=56,["CAPSLOCK"]=57,["ALT"]=58,["CTRL"]=59,
            ["F1"]=122,["F2"]=120,["F3"]=99,["F4"]=118,["F5"]=96,["F6"]=97,["F7"]=98,["F8"]=100,["F9"]=101,["F10"]=109,
            ["F11"]=103,["F12"]=111,["F13"]=105,["F14"]=107,["F15"]=113,["F16"]=106,["F17"]=64,["F18"]=79,["F19"]=80,["F20"]=90,
            ["HOME"]=115,["PAGEUP"]=116,["DELETE"]=117,["END"]=119,["PAGEDOWN"]=121,["LEFT"]=123,["RIGHT"]=124,["DOWN"]=125,["UP"]=126
        };
        static ulong Flag(string key) => key switch { "SHIFT" => 1UL << 17, "CTRL" => 1UL << 18, "ALT" => 1UL << 19, "WIN" => 1UL << 20, _ => 0 };
        static (ushort Code, bool Shift) ResolveKey(KeyboardChord chord)
        {
            var character = chord.Key switch { "PLUS" => '+', "MINUS" => '-', { Length: 1 } => chord.Key[0], _ => '\0' };
            if (character == '\0') return (Codes[chord.Key], false);
            // Resolve the current layout for each action: a user may switch AZERTY/QWERTY mid-session.
            var source = TISCopyCurrentKeyboardLayoutInputSource();
            if (source == 0) throw new IOException("Keyboard layout unavailable. Use keyboard action type for text.");
            try
            {
                var data = TISGetInputSourceProperty(source, LayoutProperty.Value);
                var layout = data == 0 ? 0 : CFDataGetBytePtr(data);
                if (layout != 0)
                    foreach (var shift in new[] { false, true })
                        for (ushort code = 0; code < 128; code++)
                        {
                            uint deadKey = 0;
                            var chars = new ushort[4];
                            uint modifiers = (chord.Modifiers.Contains("WIN") ? 1u : 0u) | (shift ? 2u : 0u);
                            if (UCKeyTranslate(layout, code, 3, modifiers, LMGetKbdType(), 1, ref deadKey, 4, out var length, chars) == 0 &&
                                length == 1 && char.ToUpperInvariant((char)chars[0]) == character)
                                return (code, shift);
                        }
                throw new ArgumentException($"Key {chord.Key} is unavailable in the active macOS keyboard layout. Use action type for text.");
            }
            finally { CFRelease(source); }
        }
        public static void Press(KeyboardChord chord)
        {
            Access();
            var keys = chord.Modifiers.Append(chord.Key).ToList();
            if (keys.Any(x => !Codes.ContainsKey(x))) throw new ArgumentException("Unsupported macOS key. Call keyboard_keys.");
            var resolved = ResolveKey(chord);
            if (resolved.Shift && !keys.Contains("SHIFT")) keys.Insert(0, "SHIFT");
            ushort Code(string key) => key == chord.Key ? resolved.Code : Codes[key];
            ulong flags = 0;
            var pressed = new List<string>();
            try
            {
                foreach (var key in keys) { flags |= Flag(key); Post(CGEventCreateKeyboardEvent(0, Code(key), true), e => CGEventSetFlags(e, flags)); pressed.Add(key); }
            }
            finally
            {
                foreach (var key in pressed.AsEnumerable().Reverse()) { flags &= ~Flag(key); Post(CGEventCreateKeyboardEvent(0, Code(key), false), e => CGEventSetFlags(e, flags)); }
            }
        }
        public static void Type(string text)
        {
            Access();
            foreach (var rune in text.EnumerateRunes())
            {
                var chars = rune.ToString().ToCharArray();
                Post(CGEventCreateKeyboardEvent(0, 0, true), e => { CGEventSetFlags(e, 0); CGEventKeyboardSetUnicodeString(e, (nuint)chars.Length, chars); });
                Post(CGEventCreateKeyboardEvent(0, 0, false), e => { CGEventSetFlags(e, 0); CGEventKeyboardSetUnicodeString(e, (nuint)chars.Length, chars); });
            }
        }
        public static void Mouse(string action, double x, double y, bool right, int clicks, int delta)
        {
            Access(); var point = new Point(x, y);
            Post(CGEventCreateMouseEvent(0, 5, point, 0));
            if (action == "scroll") Post(CGEventCreateScrollWheelEvent(0, 0, 1, -delta));
            if (action != "click") return;
            for (int i = 1; i <= clicks; i++)
            {
                Post(CGEventCreateMouseEvent(0, right ? 3u : 1u, point, right ? 1u : 0u), e => CGEventSetIntegerValueField(e, 1, i));
                Post(CGEventCreateMouseEvent(0, right ? 4u : 2u, point, right ? 1u : 0u), e => CGEventSetIntegerValueField(e, 1, i));
            }
        }
    }
    static class Win
    {
        [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Union Data; }
        [StructLayout(LayoutKind.Explicit)] struct Union { [FieldOffset(0)] public MouseData Mouse; [FieldOffset(0)] public KeyData Key; }
        [StructLayout(LayoutKind.Sequential)] struct MouseData { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
        [StructLayout(LayoutKind.Sequential)] struct KeyData { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint window);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        static void Send(params Input[] inputs) { if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        static Input Key(ushort key, bool up = false) => new() { Type = 1, Data = new Union { Key = new KeyData { Key = key, Flags = (up ? 2u : 0) | (key is >= 0x21 and <= 0x28 or 0x2C or 0x2D or 0x2E or 0x5B ? 1u : 0) } } };
        static ushort Code(string key)
        {
            if (key.Length == 1) return key[0];
            if (key.StartsWith('F') && ushort.TryParse(key[1..], out var f)) return (ushort)(0x70 + f - 1);
            return key switch { "CTRL"=>0x11,"ALT"=>0x12,"SHIFT"=>0x10,"WIN"=>0x5B,"ENTER"=>0x0D,"TAB"=>9,"ESCAPE"=>0x1B,"SPACE"=>0x20,
                "BACKSPACE"=>8,"DELETE"=>0x2E,"INSERT"=>0x2D,"HOME"=>0x24,"END"=>0x23,"PAGEUP"=>0x21,"PAGEDOWN"=>0x22,"LEFT"=>0x25,"UP"=>0x26,"RIGHT"=>0x27,"DOWN"=>0x28,"PRINTSCREEN"=>0x2C,"CAPSLOCK"=>0x14,"PLUS"=>0xBB,"MINUS"=>0xBD,_=>throw new ArgumentException("Unknown key") };
        }
        public static void Press(KeyboardChord chord)
        {
            var codes = chord.Modifiers.Append(chord.Key).Select(Code).ToArray();
            Send([.. codes.Select(x => Key(x)), .. codes.Reverse().Select(x => Key(x, true))]);
        }
        public static void Type(string text)
        {
            foreach (var ch in text)
            {
                if (ch is '\r' or '\n') { if (ch == '\n') Send(Key(13), Key(13, true)); continue; }
                Send(new Input { Type = 1, Data = new Union { Key = new KeyData { Scan = ch, Flags = 4 } } }, new Input { Type = 1, Data = new Union { Key = new KeyData { Scan = ch, Flags = 6 } } });
            }
        }
        public static void Mouse(string action, int x, int y, bool right, int clicks, int delta)
        {
            if (!SetCursorPos(x, y)) throw new Win32Exception();
            if (action == "scroll") Send(new Input { Data = new Union { Mouse = new MouseData { Flags = 0x0800, Data = unchecked((uint)-delta) } } });
            if (action == "click") for (int i = 0; i < clicks; i++) Send(
                new Input { Data = new Union { Mouse = new MouseData { Flags = right ? 8u : 2u } } },
                new Input { Data = new Union { Mouse = new MouseData { Flags = right ? 16u : 4u } } });
        }
    }
}
