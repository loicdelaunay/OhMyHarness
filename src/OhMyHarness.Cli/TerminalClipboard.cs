using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OhMyHarness.Cli;

static class TerminalClipboard
{
    const uint UnicodeText = 13;
    [DllImport("user32.dll")] static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern nint SetClipboardData(uint format, nint data);
    [DllImport("kernel32.dll")] static extern nint GetConsoleWindow();
    [DllImport("kernel32.dll")] static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll")] static extern nint GlobalFree(nint memory);
    [DllImport("kernel32.dll")] static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] static extern nuint GlobalSize(nint memory);

    public static bool Copy(string text)
    {
        if (OperatingSystem.IsMacOS()) return Mac("/usr/bin/pbcopy", text, out _);
        if (!OperatingSystem.IsWindows()) return false;
        var bytes = Encoding.Unicode.GetBytes(text.Replace("\n", "\r\n") + "\0");
        nint memory = GlobalAlloc(0x42, (nuint)bytes.Length);
        if (memory == 0) return false;
        try
        {
            nint pointer = GlobalLock(memory);
            if (pointer == 0) return false;
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { GlobalUnlock(memory); }
            if (!OpenClipboard(GetConsoleWindow())) return false;
            try
            {
                if (!EmptyClipboard() || SetClipboardData(UnicodeText, memory) == 0) return false;
                memory = 0; // Windows owns the allocation after SetClipboardData succeeds.
                return true;
            }
            finally { CloseClipboard(); }
        }
        finally { if (memory != 0) GlobalFree(memory); }
    }
    public static string? Paste()
    {
        if (OperatingSystem.IsMacOS()) return Mac("/usr/bin/pbpaste", null, out var text) ? text : null;
        if (!OperatingSystem.IsWindows() || !OpenClipboard(GetConsoleWindow())) return null;
        try
        {
            nint memory = GetClipboardData(UnicodeText);
            if (memory == 0) return "";
            nint pointer = GlobalLock(memory);
            if (pointer == 0) return null;
            try
            {
                int capacity = (int)Math.Min((ulong)GlobalSize(memory) / 2, InputBuffer.MaxLength * 2UL + 2);
                string value = Marshal.PtrToStringUni(pointer, capacity) ?? "";
                int end = value.IndexOf('\0');
                if (end >= 0) value = value[..end];
                value = value.Replace("\r\n", "\n").Replace('\r', '\n');
                return value.Length <= InputBuffer.MaxLength ? value : null;
            }
            finally { GlobalUnlock(memory); }
        }
        finally { CloseClipboard(); }
    }
    static bool Mac(string program, string? input, out string? output)
    {
        output = null;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program)
            {
                UseShellExecute = false, RedirectStandardInput = input != null, RedirectStandardOutput = input == null,
                CreateNoWindow = true
            });
            if (process == null) return false;
            var io = input == null ? Read() : Write();
            if (!io.Wait(TimeSpan.FromSeconds(1)) || !process.WaitForExit(1000)) { process.Kill(); return false; }
            output = io.Result;
            return process.ExitCode == 0;
            async Task<string?> Read() => await process.StandardOutput.ReadToEndAsync();
            async Task<string?> Write() { await process.StandardInput.WriteAsync(input); process.StandardInput.Close(); return null; }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException) { return false; }
    }
}

static class InputShortcuts
{
    public static bool HandleClipboard(InputBuffer input, ConsoleKeyInfo key, bool secret,
        Func<string, bool> copy, Func<string?> paste, Action failed)
    {
        if (!key.Modifiers.HasFlag(ConsoleModifiers.Control)) return false;
        if (key.Key == ConsoleKey.V)
        {
            var text = paste();
            if (text == null || input.Text.Length - input.SelectionLength + text.Length > InputBuffer.MaxLength) failed();
            else input.Insert(text);
            return true;
        }
        if (key.Key is ConsoleKey.C or ConsoleKey.X && input.HasSelection)
        {
            if (!secret)
            {
                if (!copy(input.SelectedText)) failed();
                else if (key.Key == ConsoleKey.X) input.DeleteSelection();
            }
            return true;
        }
        return false;
    }
}
